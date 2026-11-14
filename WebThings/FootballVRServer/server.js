
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
const HOST = process.env.HOST || "127.0.0.1";

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

function shutdown() {
    console.log("\n[Server] Encerrando servidor...");

    server.close(() => {
        database.close?.();
        console.log("[Server] Servidor encerrado.");
        process.exit(0);
    });
}

process.on("SIGINT", shutdown);
process.on("SIGTERM", shutdown);

process.on("unhandledRejection", (error) => {
    console.error("[Server] Promise não tratada:", error);
});
