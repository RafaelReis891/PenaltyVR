const express = require("express");
const http = require("http");
const cors = require("cors");
const { Server } = require("socket.io");
const path = require("path");

const database = require("./src/database");
const configureRealtime = require("./src/realtime");
const configureRoutes = require("./src/routes");

const app = express();
const server = http.createServer(app);

const PORT = Number(process.env.PORT) || 3001;
const HOST = process.env.HOST || "0.0.0.0";

app.use(cors());
app.use(express.json({ limit: "2mb" }));
app.use(express.static(path.join(__dirname, "public")));

const io = new Server(server, {
    cors: {
        origin: "*",
        methods: ["GET", "POST", "PUT", "PATCH", "DELETE"]
    }
});

const realtime = configureRealtime(io, database);
configureRoutes(app, database, realtime);

app.get("/", (req, res) => {
    res.send("FootballVR Server está funcionando!");
});

server.listen(PORT, HOST, () => {
    console.log("");
    console.log("======================================");
    console.log("       FOOTBALLVR SERVER");
    console.log("======================================");
    console.log(`HTTP:      http://${HOST}:${PORT}`);
    console.log(`WebSocket: ${HOST}:${PORT}`);
    console.log("Banco:     SQLite");
    console.log("Status:    ONLINE");
    console.log("======================================");
    console.log("");
});

// ==========================================
// VERIFICADOR DE TIMEOUT DE TURNO (Background)
// ==========================================
const TIMEOUT_CHECK_INTERVAL = 1000; // 1 segundo
let timeoutIntervalId = null;

function startTimeoutChecker() {
    timeoutIntervalId = setInterval(() => {
        try {
            const now = Date.now();
            let changed = false;

            database.runTransaction((root) => {
                const room = root.Rooms?.ARENA01;
                if (!room || !room.Match || room.Match.Status !== "Playing") {
                    return null; // Aborta, não há partida em andamento
                }

                const match = room.Match;
                const turnEnd = match.TurnStartedAt + (match.TurnDuration * 1000);

                if (now >= turnEnd) {
                    // Tempo esgotado! Avançar turno sem marcar gol.
                    const nextTurn = match.TurnNumber + 1;
                    const shotsPerPlayer = match.ShotsPerPlayer || 3;
                    
                    if (!room.ProcessedActions) room.ProcessedActions = {};
                    room.ProcessedActions[`timeout_${match.TurnNumber}`] = { type: "timeout", at: now };
                    
                    match.TurnNumber = nextTurn;
                    changed = true;

                    if (nextTurn >= shotsPerPlayer * 2) {
                        match.Status = "Finished";
                        const p1Score = room.Score?.Jogador1 || 0;
                        const p2Score = room.Score?.Jogador2 || 0;
                        if (p1Score > p2Score) match.Winner = "Jogador1";
                        else if (p2Score > p1Score) match.Winner = "Jogador2";
                        else match.Winner = "Draw";
                        
                        match.CurrentTurnPlayer = "";
                        match.TurnStartedAt = 0;
                    } else {
                        match.CurrentTurnPlayer = (match.TurnNumber % 2 !== 0) ? "Jogador2" : "Jogador1";
                        match.TurnStartedAt = now;
                    }
                }
                
                return changed ? root : null; // Só salva se houve mudança
            });

            if (changed) {
                realtime.notifyChanged("Rooms/ARENA01");
                console.log("[Timeout] Turno avançado automaticamente.");
            }
        } catch (error) {
            console.error("[Timeout] Erro na verificação:", error);
        }
    }, TIMEOUT_CHECK_INTERVAL);
}

startTimeoutChecker();

function shutdown() {
    console.log("\n[Server] Encerrando servidor...");
    if (timeoutIntervalId) clearInterval(timeoutIntervalId);
    
    server.close(() => {
        database.close?.();
        console.log("[Server] Servidor e banco de dados encerrados.");
        process.exit(0);
    });
}

process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);

process.on("unhandledRejection", (error) => {
    console.error("[Server] Promise não tratada:", error);
});