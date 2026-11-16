function resolveServerValues(value) {
    if (Array.isArray(value)) {
        return value.map(resolveServerValues);
    }
    if (value && typeof value === "object") {
        if (Object.keys(value).length === 1 && value[".sv"] === "timestamp") {
            return Date.now();
        }
        const result = {};
        for (const [key, child] of Object.entries(value)) {
            result[key] = resolveServerValues(child);
        }
        return result;
    }
    return value;
}

function configureRoutes(app, database, realtime) {
    // ==========================================
    // ENDPOINTS BÁSICOS E DE SINCRONIZAÇÃO
    // ==========================================
    app.get("/api/health", (req, res) => {
        res.json({ status: "online", server: "FootballVR", timestamp: Date.now() });
    });

    // Sincronização de tempo para cálculo do ServerTimeOffset na Unity
    app.get("/api/time", (req, res) => {
        res.json({ serverTime: Date.now() });
    });

    app.get("/api/db", (req, res) => {
        try {
            const databasePath = req.query.path || "";
            res.json({ path: databasePath, value: database.get(databasePath) });
        } catch (error) {
            res.status(400).json({ error: error.message });
        }
    });

    app.put("/api/db", (req, res) => {
        try {
            const databasePath = req.query.path || "";
            const value = resolveServerValues(req.body.value);
            database.set(databasePath, value);
            realtime.notifyChanged(databasePath);
            res.json({ success: true, path: databasePath });
        } catch (error) {
            res.status(400).json({ error: error.message });
        }
    });

    app.patch("/api/db", (req, res) => {
        try {
            const databasePath = req.query.path || "";
            const values = resolveServerValues(req.body.value);
            database.update(databasePath, values);
            realtime.notifyChanged(databasePath);
            res.json({ success: true, path: databasePath });
        } catch (error) {
            res.status(400).json({ error: error.message });
        }
    });

    app.delete("/api/db", (req, res) => {
        try {
            const databasePath = req.query.path || "";
            database.remove(databasePath);
            realtime.notifyChanged(databasePath);
            res.json({ success: true, path: databasePath });
        } catch (error) {
            res.status(400).json({ error: error.message });
        }
    });

    // ==========================================
    // LÓGICA AUTORITATIVA DA PARTIDA
    // ==========================================
    const ROOM_PATH = "Rooms/ARENA01";

    function getInitialRoom() {
        return {
            CreatedAt: Date.now(),
            Match: {
                Status: "Waiting",
                CurrentTurnPlayer: "",
                TurnNumber: 0,
                ShotsPerPlayer: 3,
                Winner: "",
                TurnStartedAt: 0,
                TurnDuration: 30,
                RestartSignal: 0
            },
            Score: { Jogador1: 0, Jogador2: 0 },
            Players: {
                Jogador1: { Name: "", Connected: false },
                Jogador2: { Name: "", Connected: false }
            },
            Actions: {},
            ProcessedActions: {}
        };
    }

    // Entrar na sala
    app.post("/api/match/join", (req, res) => {
        try {
            const { playerId, playerName } = req.body;
            if (!["Jogador1", "Jogador2"].includes(playerId)) {
                return res.status(400).json({ success: false, error: "Jogador inválido." });
            }

            database.runTransaction((root) => {
                if (!root.Rooms) root.Rooms = {};
                if (!root.Rooms.ARENA01) root.Rooms.ARENA01 = getInitialRoom();
                
                const room = root.Rooms.ARENA01;
                
                if (room.Match.Status === "Finished" && room.Match.RestartSignal === 0) {
                    return null; // Aborta: partida finalizada sem reset
                }

                if (!room.Players) room.Players = {};
                if (!room.Players[playerId]) room.Players[playerId] = {};
                
                room.Players[playerId].Name = playerName || playerId;
                room.Players[playerId].Connected = true;

                if (!room.Score) room.Score = { Jogador1: 0, Jogador2: 0 };
                if (!room.Actions) room.Actions = {};
                if (!room.ProcessedActions) room.ProcessedActions = {};

                return root;
            });

            realtime.notifyChanged(ROOM_PATH);
            res.json({ success: true, playerId });
        } catch (error) {
            res.status(500).json({ success: false, error: error.message });
        }
    });

    // Sair da sala
    app.post("/api/match/leave", (req, res) => {
        try {
            const { playerId } = req.body;
            if (!["Jogador1", "Jogador2"].includes(playerId)) {
                return res.status(400).json({ success: false, error: "Jogador inválido." });
            }

            database.runTransaction((root) => {
                const room = root.Rooms?.ARENA01;
                if (!room) return null;

                if (room.Players && room.Players[playerId]) {
                    room.Players[playerId].Connected = false;
                }

                if (room.Match && room.Match.Status === "Playing") {
                    room.Match.Status = "Waiting";
                    room.Match.CurrentTurnPlayer = "";
                    room.Match.TurnStartedAt = 0;
                }
                return root;
            });

            realtime.notifyChanged(ROOM_PATH);
            res.json({ success: true });
        } catch (error) {
            res.status(500).json({ success: false, error: error.message });
        }
    });

    // Processar chute (Lógica central autoritativa)
    app.post("/api/match/shot", (req, res) => {
        try {
            const { playerId, shotId, goal, turnNumber } = req.body;

            if (!["Jogador1", "Jogador2"].includes(playerId) || !shotId) {
                return res.status(400).json({ success: false, error: "Dados do chute inválidos." });
            }

            let shotProcessed = false;

            database.runTransaction((root) => {
                const room = root.Rooms?.ARENA01;
                if (!room || !room.Match) return null;

                const match = room.Match;

                // 1. Validar duplicidade
                if (room.ProcessedActions && room.ProcessedActions[shotId]) {
                    return null; // Aborta silenciosamente (já processado)
                }

                // 2. Validar estado da partida
                if (match.Status !== "Playing") return null;

                // 3. Validar jogador da vez
                if (match.CurrentTurnPlayer !== playerId) return null;

                // 4. Validar número do turno
                if (match.TurnNumber !== turnNumber) return null;

                // 5. Validar tempo do turno
                const now = Date.now();
                const turnEnd = match.TurnStartedAt + (match.TurnDuration * 1000);
                if (now >= turnEnd) return null; // Tempo esgotado, o intervalo de background cuidará disso

                // --- VALIDAÇÕES PASSARAM, PROCESSAR CHUTE ---
                if (!room.ProcessedActions) room.ProcessedActions = {};
                room.ProcessedActions[shotId] = { playerId, goal, processedAt: now };

                if (goal) {
                    if (!room.Score) room.Score = { Jogador1: 0, Jogador2: 0 };
                    room.Score[playerId] = (room.Score[playerId] || 0) + 1;
                }

                const nextTurn = match.TurnNumber + 1;
                const shotsPerPlayer = match.ShotsPerPlayer || 3;
                match.TurnNumber = nextTurn;
                shotProcessed = true;

                // 6. Encerrar ou avançar
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

                return root;
            });

            if (shotProcessed) {
                realtime.notifyChanged(ROOM_PATH);
            }

            res.json({ success: true, processed: shotProcessed });
        } catch (error) {
            console.error("[MATCH] Erro no chute:", error);
            res.status(500).json({ success: false, error: error.message });
        }
    });

    // Iniciar partida (Admin)
    app.post("/api/match/start", (req, res) => {
        try {
            database.runTransaction((root) => {
                const room = root.Rooms?.ARENA01;
                if (!room) return null;

                const p1Connected = room.Players?.Jogador1?.Connected;
                const p2Connected = room.Players?.Jogador2?.Connected;

                if (!p1Connected || !p2Connected) return null; // Ambos devem estar conectados

                room.Match.Status = "Playing";
                room.Match.CurrentTurnPlayer = "Jogador1";
                room.Match.TurnNumber = 0;
                room.Match.TurnStartedAt = Date.now();
                
                return root;
            });
            realtime.notifyChanged(ROOM_PATH);
            res.json({ success: true });
        } catch (error) {
            res.status(500).json({ success: false, error: error.message });
        }
    });

    // Reiniciar partida (Admin)
    app.post("/api/match/restart", (req, res) => {
        try {
            database.runTransaction((root) => {
                const room = root.Rooms?.ARENA01;
                if (!room) return null;

                room.Match.Status = "Waiting";
                room.Match.CurrentTurnPlayer = "";
                room.Match.TurnNumber = 0;
                room.Match.TurnStartedAt = 0;
                room.Match.Winner = "";
                room.Match.RestartSignal = Date.now(); // Sinal para a Unity

                room.Score = { Jogador1: 0, Jogador2: 0 };
                room.Actions = {};
                room.ProcessedActions = {};

                return root;
            });
            realtime.notifyChanged(ROOM_PATH);
            res.json({ success: true });
        } catch (error) {
            res.status(500).json({ success: false, error: error.message });
        }
    });

        // Encerrar partida manualmente (Admin)
    app.post("/api/match/finish", (req, res) => {
        try {
            database.runTransaction((root) => {
                const room = root.Rooms?.ARENA01;
                if (!room || !room.Match) return null;

                room.Match.Status = "Finished";
                if (!room.Match.Winner || room.Match.Winner === "") {
                    const p1Score = room.Score?.Jogador1 || 0;
                    const p2Score = room.Score?.Jogador2 || 0;
                    if (p1Score > p2Score) room.Match.Winner = "Jogador1";
                    else if (p2Score > p1Score) room.Match.Winner = "Jogador2";
                    else room.Match.Winner = "Draw";
                }
                room.Match.CurrentTurnPlayer = "";
                room.Match.TurnStartedAt = 0;
                return root;
            });
            realtime.notifyChanged("Rooms/ARENA01");
            res.json({ success: true });
        } catch (error) {
            res.status(500).json({ success: false, error: error.message });
        }
    });

        // 1. Resetar partida: Limpa os dados da partida e volta para "Waiting"
    app.post("/api/match/reset", (req, res) => {
        try {
            database.runTransaction((root) => {
                const room = root.Rooms?.ARENA01;
                if (!room) return null;

                room.Match.Status = "Waiting";
                room.Match.CurrentTurnPlayer = "";
                room.Match.TurnNumber = 0;
                room.Match.TurnStartedAt = 0;
                room.Match.Winner = "";
                // Mantemos o RestartSignal antigo para não disparar reinício inesperado na Unity
                
                room.Score = { Jogador1: 0, Jogador2: 0 };
                room.Actions = {};
                room.ProcessedActions = {};

                return root;
            });
            realtime.notifyChanged("Rooms/ARENA01");
            res.json({ success: true });
        } catch (error) {
            res.status(500).json({ success: false, error: error.message });
        }
    });

    // 2. Reiniciar partida: Apenas envia o sinal para a Unity reiniciar
    app.post("/api/match/restart", (req, res) => {
        try {
            database.runTransaction((root) => {
                const room = root.Rooms?.ARENA01;
                if (!room || !room.Match) return null;

                // Atualiza o sinal de reinício (a Unity detecta quando esse valor aumenta)
                room.Match.RestartSignal = Date.now();
                return root;
            });
            realtime.notifyChanged("Rooms/ARENA01");
            res.json({ success: true });
        } catch (error) {
            res.status(500).json({ success: false, error: error.message });
        }
    });

    // Limpar sala completamente (Admin)
    app.delete("/api/match/room", (req, res) => {
        try {
            database.runTransaction((root) => {
                if (root.Rooms && root.Rooms.ARENA01) {
                    delete root.Rooms.ARENA01;
                }
                return root;
            });
            realtime.notifyChanged("Rooms");
            res.json({ success: true });
        } catch (error) {
            res.status(500).json({ success: false, error: error.message });
        }
    });
}

module.exports = configureRoutes;