import { initializeApp } from "https://www.gstatic.com/firebasejs/13.2.0/firebase-app.js";
import { getAuth, signInAnonymously } from "https://www.gstatic.com/firebasejs/13.2.0/firebase-auth.js";
import {
    getDatabase,
    ref,
    get,
    update,
    onValue,
    runTransaction,
    push,
    onDisconnect,
    serverTimestamp
} from "https://www.gstatic.com/firebasejs/13.2.0/firebase-database.js";

const firebaseConfig = {
    apiKey: "AIzaSyDR_txGYREP2FDfkSdy4DwDIfcFHqoV1aM",
    authDomain: "elasnojogo-7a990.firebaseapp.com",
    databaseURL: "https://elasnojogo-7a990-default-rtdb.firebaseio.com",
    projectId: "elasnojogo-7a990",
    storageBucket: "elasnojogo-7a990.firebasestorage.app",
    messagingSenderId: "258174385657",
    appId: "1:258174385657:web:429ffc560317a795e61128"
};

const app = initializeApp(firebaseConfig);
const auth = getAuth(app);
const database = getDatabase(app);

const FIXED_ROOM_CODE = "ARENA01";
const PLAYER_ID = "Jogador2";
const SESSION_ID = crypto.randomUUID();

const $ = id => document.getElementById(id);
const ui = {
    connection: $("connectionStatus"),
    joinButton: $("joinButton"),
    message: $("message"),
    joinPanel: $("joinPanel"),
    gamePanel: $("gamePanel"),
    roomTitle: $("roomTitle"),
    matchStatus: $("matchStatus"),
    hostScore: $("hostScore"),
    guestScore: $("guestScore"),
    hostShots: $("hostShots"),
    guestShots: $("guestShots"),
    turnName: $("turnName"),
    turnDescription: $("turnDescription"),
    timer: $("timer"),
    timerStatus: $("timerStatus"),
    shotButton: $("shotButton"),
    shotMessage: $("shotMessage"),
    turnCount: $("turnCount"),
    progressBar: $("progressBar"),
    playerUID: $("playerUID"),
    winner: $("winner"),
    leaveButton: $("leaveButton"),
    roleHost: $("roleHost"),
    roleGuest: $("roleGuest"),
    debugBox: $("debugBox")
};

let currentUser = null;
let roomRef = null;
let roomData = null;
let currentMatch = null;
let unsubscribeRoom = null;
let connectedToRoom = false;
let databaseConnected = false;
let sendingShot = false;
let serverTimeOffset = 0;
let submittedTurnNumber = -1;
let presencePromise = null;
let disconnectHandlers = [];

function setMessage(message, type = "") {
    ui.message.textContent = message;
    ui.message.className = type;
}

function setConnection(connected, text) {
    ui.connection.className = `status ${connected ? "online" : "offline"}`;
    ui.connection.replaceChildren(
        document.createElement("span"),
        document.createTextNode(` ${text}`)
    );

    ui.joinButton.disabled = !connected || !currentUser;

    if (!connected && connectedToRoom) {
        ui.shotButton.disabled = true;
        ui.shotMessage.textContent = "Conexão perdida. Aguardando reconexão com o Firebase...";
    } else if (connectedToRoom && roomData) {
        updateInterface();
    }
}

function initRoleSelection() {
    // A página web é exclusivamente o Guest. O Host precisa permanecer na Unity,
    // pois é o cliente Unity que processa Actions e controla os timeouts.
    if (ui.roleHost) {
        ui.roleHost.checked = false;
        ui.roleHost.disabled = true;
    }

    if (ui.roleGuest) {
        ui.roleGuest.checked = true;
        ui.roleGuest.disabled = true;
    }
}

async function initializeFirebase() {
    try {
        setMessage("Autenticando no Firebase...");

        const result = await signInAnonymously(auth);
        currentUser = result.user;
        ui.playerUID.textContent = PLAYER_ID;

        onValue(ref(database, ".info/serverTimeOffset"), snapshot => {
            serverTimeOffset = Number(snapshot.val() || 0);
            updateTurnTimer();
        });

        onValue(ref(database, ".info/connected"), snapshot => {
            databaseConnected = snapshot.val() === true;
            setConnection(databaseConnected, databaseConnected ? "Online" : "Reconectando...");

            if (databaseConnected && connectedToRoom) {
                refreshPresence().catch(error => {
                    console.error("[WebSimulator] Erro ao restaurar presença:", error);
                    setMessage("Falha ao restaurar presença: " + error.message, "error");
                });
            }
        });

        setMessage("Autenticado. Aguardando conexão com o banco de dados...");
    } catch (error) {
        console.error("[WebSimulator] Authentication:", error);
        setConnection(false, "Erro de autenticação");
        setMessage(`${error.code || "Firebase"}: ${error.message}`, "error");
    }
}

async function registerDisconnectHandlersAndSetOnline() {
    const sessionConnectedRef = ref(database, `Rooms/${FIXED_ROOM_CODE}/Players/${PLAYER_ID}/Sessions/${SESSION_ID}/Connected`);
    const handler = onDisconnect(sessionConnectedRef);
    await handler.set(false);
    disconnectHandlers = [handler];
}

async function claimGuestSlot() {
    if (!roomRef || !currentUser) throw new Error("A sala ou a autenticação não está definida.");

    const transaction = await runTransaction(roomRef, currentRoom => {
        if (!currentRoom || !currentRoom.Match) return;
        const match = currentRoom.Match;
        const status = match.Status || "Waiting";
        const slotId = match.GuestUid || "";
        if (slotId && slotId !== PLAYER_ID) return;

        currentRoom.Players = currentRoom.Players || {};
        if (Object.keys(currentRoom.Players).some(id => id !== "Jogador1" && id !== "Jogador2")) return;
        const player = currentRoom.Players[PLAYER_ID] || {};
        const activeSessionId = player.ActiveSessionId || "";
        const activeConnected = !!(activeSessionId && player.Sessions?.[activeSessionId]?.Connected === true);

        if (activeSessionId && activeSessionId !== SESSION_ID && activeConnected) return;
        if (status === "Finished" && activeSessionId !== SESSION_ID) return;

        match.GuestUid = PLAYER_ID;
        player.Name = "Jogador 2 (Web Guest)";
        player.Role = "Guest";
        player.ActiveSessionId = SESSION_ID;
        player.Connected = true;
        player.Sessions = player.Sessions || {};
        player.Sessions[SESSION_ID] = { Connected: true };
        currentRoom.Players[PLAYER_ID] = player;
        return currentRoom;
    }, { applyLocally: false });

    const confirmedRoom = transaction.snapshot.val();
    if (!transaction.committed || confirmedRoom?.Match?.GuestUid !== PLAYER_ID ||
        confirmedRoom?.Players?.[PLAYER_ID]?.ActiveSessionId !== SESSION_ID) {
        throw new Error("A vaga Jogador2 já está ocupada por outra sessão ativa ou a partida não permite entrada.");
    }
}

async function refreshPresence() {
    if (!connectedToRoom || !roomRef || !databaseConnected || !currentUser)
        return;

    if (presencePromise)
        return presencePromise;

    presencePromise = (async () => {
        await registerDisconnectHandlersAndSetOnline();
        await claimGuestSlot();
        setMessage("Conectado à sala como Jogador 2.", "success");
        updateInterface();
    })();

    try {
        await presencePromise;
    } finally {
        presencePromise = null;
    }
}

async function joinRoom() {
    if (!currentUser) {
        setMessage("A autenticação ainda não terminou.", "error");
        return;
    }

    if (!databaseConnected) {
        setMessage("O Firebase ainda não está conectado ao banco de dados.", "error");
        return;
    }

    if (connectedToRoom)
        return;

    ui.joinButton.disabled = true;
    setMessage("Verificando a sala ARENA01...");

    try {
        const targetRoomRef = ref(database, `Rooms/${FIXED_ROOM_CODE}`);
        const snapshot = await get(targetRoomRef);

        if (!snapshot.exists())
            throw new Error("A sala ainda não foi criada. Abra primeiro o jogo Unity configurado como Host.");

        const initialRoom = snapshot.val();
        if (!initialRoom?.Match)
            throw new Error("A estrutura da sala está incompleta. Resete a sala pelo painel Admin.");

        roomRef = targetRoomRef;
        connectedToRoom = true;

        await refreshPresence();

        ui.joinPanel.classList.add("hidden");
        ui.gamePanel.classList.remove("hidden");
        ui.roomTitle.textContent = `Arena: ${FIXED_ROOM_CODE} (Guest)`;

        unsubscribeRoom = onValue(roomRef, snapshotRoom => {
            roomData = snapshotRoom.val();
            currentMatch = roomData?.Match || null;

            if (currentMatch && currentMatch.TurnNumber !== submittedTurnNumber)
                submittedTurnNumber = -1;

            updateInterface();
            updateTurnTimer();
        }, error => {
            console.error("[WebSimulator] Erro no listener da sala:", error);
            setMessage("Erro ao acompanhar a sala: " + error.message, "error");
        });

        setMessage("Você entrou como Jogador 2 (Guest).", "success");
    } catch (error) {
        console.error("[WebSimulator] Join Error:", error);

        if (connectedToRoom)
            await releaseRoomMembership(false);

        setMessage(error.message, "error");
    } finally {
        ui.joinButton.disabled = !databaseConnected;
    }
}

function updateInterface() {
    if (!roomData || !currentUser || !connectedToRoom)
        return;

    const match = roomData.Match || {};
    const score = roomData.Score || {};
    const turnNumber = Number(match.TurnNumber || 0);
    const shotsPerPlayer = Math.max(1, Number(match.ShotsPerPlayer || 5));
    const totalShots = shotsPerPlayer * 2;
    const status = match.Status || "Waiting";

    const hostShots = Math.min(Math.ceil(turnNumber / 2), shotsPerPlayer);
    const guestShots = Math.min(Math.floor(turnNumber / 2), shotsPerPlayer);

    ui.hostScore.textContent = Number(score.Host || 0);
    ui.guestScore.textContent = Number(score.Guest || 0);
    ui.hostShots.textContent = hostShots;
    ui.guestShots.textContent = guestShots;
    ui.turnCount.textContent = `${turnNumber} / ${totalShots} chutes`;
    ui.progressBar.style.width = `${Math.min(100, (turnNumber / totalShots) * 100)}%`;

    const winnerUid = match.Winner || "";
    if (winnerUid === "Draw")
        ui.winner.textContent = "EMPATE";
    else if (winnerUid)
        ui.winner.textContent = winnerUid === match.HostUid ? "Jogador 1" : "Jogador 2";
    else
        ui.winner.textContent = "---";

    const isMyTurn = status === "Playing" && match.CurrentTurnUid === PLAYER_ID;
    const isOpponentTurn = status === "Playing" && !!match.CurrentTurnUid && !isMyTurn;
    const canShoot = databaseConnected && isMyTurn && !sendingShot && submittedTurnNumber !== turnNumber;

    const safeStatus = escapeHtml(status);
    const safeCurrentTurn = escapeHtml(match.CurrentTurnUid || "VAZIO");
    const safeGuestUid = escapeHtml(match.GuestUid || "VAZIO");

    if (ui.debugBox) {
        ui.debugBox.innerHTML = `
            <strong>DEBUG DE ESTADO</strong><br>
            • Meu papel: <span style="color:#0ff">GUEST</span><br>
            • Meu ID lógico: <span style="color:#fff">${escapeHtml(PLAYER_ID)}</span><br>
            • GuestUid da sala: <span style="color:${match.GuestUid === PLAYER_ID ? "#0f0" : "#f00"}">${safeGuestUid}</span><br>
            • Vez atual: <span style="color:#fff">${safeCurrentTurn}</span><br>
            • Status: <span style="color:#ff0">${safeStatus}</span><br>
            • É minha vez? <strong style="color:${isMyTurn ? "#0f0" : "#f00"}">${isMyTurn ? "SIM" : "NÃO"}</strong><br>
            • Banco conectado? <strong>${databaseConnected ? "SIM" : "NÃO"}</strong>
        `;
    }

    ui.matchStatus.className = `badge ${status === "Playing" ? "playing" : status === "Finished" ? "finished" : "waiting"}`;
    ui.matchStatus.textContent = status === "Playing" ? "EM ANDAMENTO" : status === "Finished" ? "FINALIZADA" : "AGUARDANDO";

    if (status === "Finished") {
        ui.turnName.textContent = "Partida finalizada";
        ui.turnDescription.textContent = "Todos os chutes foram realizados.";
        ui.shotButton.disabled = true;
        ui.shotMessage.textContent = "Partida encerrada.";
    } else if (status === "Waiting") {
        ui.turnName.textContent = "Aguardando início";
        ui.turnDescription.textContent = "Os dois jogadores precisam estar conectados. O Admin inicia a partida.";
        ui.shotButton.disabled = true;
        ui.shotMessage.textContent = "Aguardando o painel Admin iniciar a partida.";
    } else if (isMyTurn) {
        ui.turnName.textContent = "Jogador 2 — sua vez!";
        ui.turnDescription.textContent = "Você pode realizar o chute.";
        ui.shotButton.disabled = !canShoot;
        ui.shotMessage.textContent = submittedTurnNumber === turnNumber
            ? "Chute enviado. Aguardando o processamento da Unity..."
            : databaseConnected
                ? "Clique para sortear gol ou erro."
                : "Aguardando conexão com o Firebase.";
    } else if (isOpponentTurn) {
        ui.turnName.textContent = "Aguardando o Jogador 1 (Unity)...";
        ui.turnDescription.textContent = "Aguardando o chute do adversário.";
        ui.shotButton.disabled = true;
        ui.shotMessage.textContent = "O Jogador 1 está com a vez.";
    } else {
        ui.turnName.textContent = "Aguardando adversário";
        ui.turnDescription.textContent = "Aguardando atualização do turno.";
        ui.shotButton.disabled = true;
    }
}

function updateTurnTimer() {
    if (!ui.timer || !currentMatch)
        return;

    if (currentMatch.Status !== "Playing") {
        ui.timer.textContent = currentMatch.Status === "Finished" ? "Finalizado" : "--:--";
        ui.timerStatus.textContent = currentMatch.Status === "Finished" ? "Partida encerrada" : "Aguardando Admin";
        return;
    }

    const startedAt = Number(currentMatch.TurnStartedAt || 0);
    const duration = Math.max(1, Number(currentMatch.TurnDuration || 30));

    if (startedAt <= 0) {
        ui.timer.textContent = "--:--";
        ui.timerStatus.textContent = "Aguardando";
        return;
    }

    const serverNow = Date.now() + serverTimeOffset;
    const remaining = Math.max(0, Math.ceil((duration * 1000 - (serverNow - startedAt)) / 1000));
    const minutes = Math.floor(remaining / 60);
    const seconds = remaining % 60;

    ui.timer.textContent = `${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
    ui.timerStatus.textContent = remaining <= 0 ? "Tempo esgotado" : "Tempo restante";
    ui.timer.style.color = remaining <= 10 ? "var(--red)" : "var(--green)";
}

async function simulateShot() {
    if (!connectedToRoom || !databaseConnected || !roomRef || !roomData || sendingShot)
        return;

    const match = roomData.Match || {};
    const turnNumber = Number(match.TurnNumber || 0);

    if (match.Status !== "Playing" || match.CurrentTurnUid !== PLAYER_ID) {
        setMessage("Não é sua vez de chutar.", "error");
        return;
    }

    if (submittedTurnNumber === turnNumber) {
        setMessage("Este turno já teve um chute enviado.", "error");
        return;
    }

    sendingShot = true;
    updateInterface();

    const goal = Math.random() < 0.5;
    const actionRef = push(ref(database, `Rooms/${FIXED_ROOM_CODE}/Actions`));
    const shotData = {
        ShotId: actionRef.key,
        PlayerUid: PLAYER_ID,
        Goal: goal,
        TurnNumber: turnNumber,
        CreatedAt: serverTimestamp()
    };

    try {
        await update(actionRef, shotData);
        submittedTurnNumber = turnNumber;

        const result = goal ? "GOL!" : "ERROU!";
        ui.shotMessage.textContent = `Resultado enviado: ${result}. Aguardando a Unity.`;
        setMessage(`${result} Resultado enviado para processamento.`, "success");
    } catch (error) {
        console.error("[WebSimulator] Shot:", error);
        setMessage(`Erro ao enviar chute: ${error.message}`, "error");
    } finally {
        sendingShot = false;
        updateInterface();
    }
}

async function releaseRoomMembership(showMessage = true) {
    if (!roomRef || !currentUser)
        return;

    const oldRoomRef = roomRef;
    const oldUid = PLAYER_ID;

    connectedToRoom = false;
    sendingShot = false;
    submittedTurnNumber = -1;

    if (unsubscribeRoom) {
        unsubscribeRoom();
        unsubscribeRoom = null;
    }

    try {
        if (databaseConnected) {
            await runTransaction(oldRoomRef, currentRoomValue => {
                if (!currentRoomValue?.Match)
                    return currentRoomValue;

                const match = currentRoomValue.Match;
                const player = currentRoomValue.Players?.[oldUid];
                const ownsCurrentSession = player?.ActiveSessionId === SESSION_ID;
                if (ownsCurrentSession) {
                    if (match.GuestUid === oldUid) match.GuestUid = "";
                    delete currentRoomValue.Players[oldUid];
                    if (match.Status === "Playing") {
                        match.Status = "Waiting";
                        match.CurrentTurnUid = "";
                        match.TurnStartedAt = 0;
                    }
                }

                return currentRoomValue;
            }, { applyLocally: false });

            for (const handler of disconnectHandlers) {
                try {
                    await handler.cancel();
                } catch (cancelError) {
                    console.warn("[WebSimulator] Não foi possível cancelar um onDisconnect:", cancelError);
                }
            }
        }
    } catch (error) {
        console.warn("[WebSimulator] Erro ao liberar a vaga. O onDisconnect poderá concluir a limpeza:", error);
    }

    disconnectHandlers = [];
    roomRef = null;
    roomData = null;
    currentMatch = null;
    presencePromise = null;

    ui.gamePanel.classList.add("hidden");
    ui.joinPanel.classList.remove("hidden");
    ui.shotButton.disabled = true;
    ui.joinButton.disabled = !databaseConnected;

    if (showMessage)
        setMessage("Você saiu da sala. A vaga do Jogador 2 foi liberada.", "success");
}

async function leaveRoom() {
    if (!connectedToRoom)
        return;

    await releaseRoomMembership(true);
}

function escapeHtml(value) {
    return String(value ?? "").replace(/[&<>"']/g, character => ({
        "&": "&amp;",
        "<": "&lt;",
        ">": "&gt;",
        '"': "&quot;",
        "'": "&#39;"
    })[character]);
}

ui.joinButton.addEventListener("click", joinRoom);
ui.shotButton.addEventListener("click", simulateShot);
ui.leaveButton.addEventListener("click", leaveRoom);

setInterval(updateTurnTimer, 200);
initRoleSelection();
initializeFirebase();
