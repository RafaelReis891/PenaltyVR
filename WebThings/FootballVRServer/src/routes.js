
function resolveServerValues(value) {
    if (Array.isArray(value)) {
        return value.map(resolveServerValues);
    }

    if (value && typeof value === "object") {
        if (
            Object.keys(value).length === 1 &&
            value[".sv"] === "timestamp"
        ) {
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
    app.get("/api/health", (req, res) => {
        res.json({
            status: "online",
            server: "FootballVR",
            timestamp: Date.now()
        });
    });

    app.get("/api/db", (req, res) => {
        try {
            const databasePath = req.query.path || "";

            res.json({
                path: databasePath,
                value: database.get(databasePath)
            });
        } catch (error) {
            res.status(400).json({
                error: error.message
            });
        }
    });

    app.put("/api/db", (req, res) => {
        try {
            const databasePath = req.query.path || "";
            const value = resolveServerValues(req.body.value);

            database.set(databasePath, value);
            realtime.notifyChanged(databasePath);

            res.json({
                success: true,
                path: databasePath
            });
        } catch (error) {
            res.status(400).json({
                error: error.message
            });
        }
    });

    app.patch("/api/db", (req, res) => {
        try {
            const databasePath = req.query.path || "";
            const values = resolveServerValues(req.body.value);

            database.update(databasePath, values);
            realtime.notifyChanged(databasePath);

            res.json({
                success: true,
                path: databasePath
            });
        } catch (error) {
            res.status(400).json({
                error: error.message
            });
        }
    });

    app.delete("/api/db", (req, res) => {
        try {
            const databasePath = req.query.path || "";

            database.remove(databasePath);
            realtime.notifyChanged(databasePath);

            res.json({
                success: true,
                path: databasePath
            });
        } catch (error) {
            res.status(400).json({
                error: error.message
            });
        }
    });

    app.post("/api/transaction", (req, res) => {
        try {
            const {
                path: databasePath = "",
                expected,
                value
            } = req.body;

            const resolvedValue = resolveServerValues(value);

            const committed = database.compareAndSet(
                databasePath,
                expected,
                resolvedValue
            );

            if (committed) {
                realtime.notifyChanged(databasePath);
            }

            res.json({
                success: true,
                committed,
                value: database.get(databasePath)
            });
        } catch (error) {
            res.status(400).json({
                error: error.message
            });
        }
    });

    // ==========================================
    // SIMULADOR FOOTBALLVR
    // ==========================================

    // Registrar um chute.
    app.post("/api/test/shot", (req, res) => {
        try {
            const { player } = req.body;

            if (!["Player1", "Player2"].includes(player)) {
                return res.status(400).json({
                    success: false,
                    error: "Jogador inválido."
                });
            }

            const playerNumber =
                player === "Player1" ? "Jogador1" : "Jogador2";

            // Sorteio realizado no servidor: 50% de chance de gol.
            const goal = Math.random() < 0.5;

            const roomPath = "Rooms/ARENA01";

            const currentScore =
                database.get(`${roomPath}/Score/${playerNumber}`) || 0;

            const currentShots =
                database.get(`${roomPath}/Stats/${playerNumber}/Shots`) || 0;

            const currentGoals =
                database.get(`${roomPath}/Stats/${playerNumber}/Goals`) || 0;

            const newScore = Number(currentScore) + (goal ? 1 : 0);
            const newShots = Number(currentShots) + 1;
            const newGoals = Number(currentGoals) + (goal ? 1 : 0);

            database.update(roomPath, {
                [`Score/${playerNumber}`]: newScore,
                [`Stats/${playerNumber}/Shots`]: newShots,
                [`Stats/${playerNumber}/Goals`]: newGoals,
                "Match/Status": "Playing"
            });

            realtime.notifyChanged(roomPath);

            console.log(
                `[SIMULATOR] ${player} chutou: ${goal ? "GOL!" : "DEFENDIDO"}`
            );

            res.json({
                success: true,
                player,
                result: goal ? "Goal" : "Saved",
                score: newScore,
                shots: newShots,
                goals: newGoals
            });

        } catch (error) {
            console.error("[SIMULATOR] Erro no chute:", error);

            res.status(500).json({
                success: false,
                error: error.message
            });
        }
    });

    // Registrar jogador no simulador.
    app.post("/api/test/join", (req, res) => {
        try {
            const { player } = req.body;

            if (!["Player1", "Player2"].includes(player)) {
                return res.status(400).json({
                    success: false,
                    error: "Jogador inválido."
                });
            }

            database.update(`Rooms/ARENA01/Players/${player}`, {
                Name: player,
                Connected: true,
                LastSeen: Date.now(),
                Source: "Simulator"
            });

            realtime.notifyChanged("Rooms/ARENA01");

            res.json({
                success: true,
                player
            });

        } catch (error) {
            res.status(500).json({
                success: false,
                error: error.message
            });
        }
    });

    // Reiniciar partida sem remover os jogadores.
    app.post("/api/test/restart", (req, res) => {
        try {
            const roomPath = "Rooms/ARENA01";

            database.set(`${roomPath}/Score`, {
                Jogador1: 0,
                Jogador2: 0
            });

            database.set(`${roomPath}/Stats`, {
                Jogador1: {
                    Shots: 0,
                    Goals: 0
                },
                Jogador2: {
                    Shots: 0,
                    Goals: 0
                }
            });

            database.update(`${roomPath}/Match`, {
                Status: "Waiting",
                RestartSignal: Date.now()
            });

            realtime.notifyChanged(roomPath);

            res.json({
                success: true
            });

        } catch (error) {
            res.status(500).json({
                success: false,
                error: error.message
            });
        }
    });

    // Limpar completamente a sala.
    app.delete("/api/test/room", (req, res) => {
        try {
            database.remove("Rooms/ARENA01");

            realtime.notifyChanged("Rooms/ARENA01");

            res.json({
                success: true
            });

        } catch (error) {
            res.status(500).json({
                success: false,
                error: error.message
            });
        }
    });
}

module.exports = configureRoutes;
