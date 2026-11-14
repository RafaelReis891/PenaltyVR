
function normalize(value = "") {
    return String(value).replace(/^\/+|\/+$/g, "");
}

function pathsOverlap(a, b) {
    if (!a || !b) return true;

    return (
        a === b ||
        a.startsWith(b + "/") ||
        b.startsWith(a + "/")
    );
}

function configureRealtime(io, database) {
    const subscriptions = new Map();

    io.on("connection", (socket) => {
        console.log(`[Socket] Cliente conectado: ${socket.id}`);

        subscriptions.set(socket.id, new Set());

        socket.on("subscribe", (payload = {}) => {
            try {
                const databasePath = normalize(payload.path);
                const value = database.get(databasePath);

                subscriptions.get(socket.id).add(databasePath);

                socket.emit("value", {
                    path: databasePath,
                    value
                });

                console.log(`[Socket] Inscrição: ${databasePath}`);
            } catch (error) {
                console.error("[Socket] Erro:", error);

                socket.emit("database:error", {
                    message: error.message
                });
            }
        });

        socket.on("unsubscribe", (payload = {}) => {
            const databasePath = normalize(payload.path);

            subscriptions.get(socket.id)?.delete(databasePath);
        });

        socket.on("disconnect", () => {
            subscriptions.delete(socket.id);
            console.log(`[Socket] Cliente desconectado: ${socket.id}`);
        });
    });

    function notifyChanged(changedPath) {
        const normalizedChangedPath = normalize(changedPath);

        for (const [socketId, paths] of subscriptions.entries()) {
            const socket = io.sockets.sockets.get(socketId);

            if (!socket) continue;

            for (const subscribedPath of paths) {
                if (!pathsOverlap(subscribedPath, normalizedChangedPath)) {
                    continue;
                }

                socket.emit("value", {
                    path: subscribedPath,
                    value: database.get(subscribedPath)
                });
            }
        }
    }

    return {
        notifyChanged
    };
}

module.exports = configureRealtime;
