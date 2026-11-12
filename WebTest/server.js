
const http = require("http");
const fs = require("fs");
const path = require("path");
const os = require("os");

const PORT = 5500;
const HOST = "0.0.0.0";
const ROOT = __dirname;

const MIME_TYPES = {
    ".html": "text/html; charset=utf-8",
    ".css": "text/css; charset=utf-8",
    ".js": "application/javascript; charset=utf-8",
    ".json": "application/json; charset=utf-8",
    ".png": "image/png",
    ".jpg": "image/jpeg",
    ".jpeg": "image/jpeg",
    ".gif": "image/gif",
    ".svg": "image/svg+xml",
    ".ico": "image/x-icon",
    ".webp": "image/webp",
    ".mp3": "audio/mpeg",
    ".wav": "audio/wav",
    ".mp4": "video/mp4",
    ".webmanifest": "application/manifest+json",
    ".txt": "text/plain; charset=utf-8"
};

function getLocalIP() {
    const interfaces = os.networkInterfaces();

    for (const name of Object.keys(interfaces)) {
        for (const net of interfaces[name] || []) {
            if (net.family === "IPv4" && !net.internal) {
                return net.address;
            }
        }
    }

    return "127.0.0.1";
}

const server = http.createServer((req, res) => {
    try {
        const pathname = decodeURIComponent(
            new URL(req.url, `http://${req.headers.host}`).pathname
        );

        const relativePath = pathname === "/"
            ? "index.html"
            : pathname.replace(/^\/+/, "");

        const filePath = path.resolve(ROOT, relativePath);

        // Impede acesso a arquivos fora da pasta do servidor.
        if (filePath !== ROOT &&
            !filePath.startsWith(ROOT + path.sep)) {
            res.writeHead(403);
            res.end("Acesso negado");
            return;
        }

        fs.stat(filePath, (statError, stat) => {
            if (statError || !stat.isFile()) {
                res.writeHead(404, {
                    "Content-Type": "text/plain; charset=utf-8"
                });
                res.end("Arquivo não encontrado");
                return;
            }

            const extension = path.extname(filePath).toLowerCase();
            const contentType =
                MIME_TYPES[extension] || "application/octet-stream";

            res.writeHead(200, {
                "Content-Type": contentType,
                "Cache-Control": "no-cache"
            });

            fs.createReadStream(filePath).pipe(res);
        });

    } catch (error) {
        res.writeHead(400);
        res.end("Requisição inválida");
    }
});

server.listen(PORT, HOST, () => {
    const ip = getLocalIP();

    console.log("");
    console.log("======================================");
    console.log("       FOOTBALL VR - SERVIDOR");
    console.log("======================================");
    console.log("");
    console.log(`Servidor iniciado na porta ${PORT}`);
    console.log("");
    console.log("Acesso pelo computador:");
    console.log(`http://localhost:${PORT}/`);
    console.log("");
    console.log("Acesso pela rede:");
    console.log(`http://${ip}:${PORT}/`);
    console.log("");
    console.log("Mantenha esta janela aberta.");
    console.log("Para encerrar, pressione CTRL + C.");
    console.log("======================================");
    console.log("");
});

server.on("error", (error) => {
    console.error("Erro ao iniciar o servidor:", error.message);
    process.exit(1);
});
