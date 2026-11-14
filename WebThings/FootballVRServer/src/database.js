
const fs = require("fs");
const path = require("path");
const Database = require("better-sqlite3");

const databaseFolder = __dirname;
const databasePath = path.join(databaseFolder, "footballvr.db");

if (!fs.existsSync(databaseFolder)) {
    fs.mkdirSync(databaseFolder, { recursive: true });
}

const db = new Database(databasePath);

db.pragma("journal_mode = WAL");
db.pragma("synchronous = NORMAL");

db.exec(`
    CREATE TABLE IF NOT EXISTS app_data (
        id INTEGER PRIMARY KEY CHECK (id = 1),
        data TEXT NOT NULL
    );
`);

db.prepare(`
    INSERT OR IGNORE INTO app_data (id, data)
    VALUES (1, '{}')
`).run();

function normalizePath(databasePath = "") {
    if (typeof databasePath !== "string") {
        throw new Error("O caminho precisa ser uma string.");
    }

    const cleanPath = databasePath.replace(/^\/+|\/+$/g, "");

    if (!cleanPath) return [];

    const segments = cleanPath.split("/");

    for (const segment of segments) {
        if (!segment || /[.#$[\]]/.test(segment)) {
            throw new Error(`Caminho inválido: ${databasePath}`);
        }
    }

    return segments;
}

function getRoot() {
    const row = db.prepare(
        "SELECT data FROM app_data WHERE id = 1"
    ).get();

    return JSON.parse(row.data);
}

function saveRoot(data) {
    db.prepare(`
        UPDATE app_data
        SET data = ?
        WHERE id = 1
    `).run(JSON.stringify(data));
}

function getAtPath(root, segments) {
    let current = root;

    for (const segment of segments) {
        if (
            current === null ||
            typeof current !== "object" ||
            !Object.prototype.hasOwnProperty.call(current, segment)
        ) {
            return null;
        }

        current = current[segment];
    }

    return current === undefined ? null : current;
}

function setAtPath(root, segments, value) {
    if (segments.length === 0) {
        return value;
    }

    let current = root;

    for (let i = 0; i < segments.length - 1; i++) {
        const segment = segments[i];

        if (
            current[segment] === null ||
            typeof current[segment] !== "object" ||
            Array.isArray(current[segment])
        ) {
            current[segment] = {};
        }

        current = current[segment];
    }

    const finalSegment = segments[segments.length - 1];

    if (value === null) {
        delete current[finalSegment];
    } else {
        current[finalSegment] = value;
    }

    return root;
}

function get(databasePath = "") {
    return getAtPath(getRoot(), normalizePath(databasePath));
}

function set(databasePath, value) {
    const segments = normalizePath(databasePath);

    const transaction = db.transaction(() => {
        const root = getRoot();
        const updatedRoot = setAtPath(root, segments, value);

        saveRoot(updatedRoot);
    });

    transaction();
}

function update(databasePath, values) {
    if (
        !values ||
        typeof values !== "object" ||
        Array.isArray(values)
    ) {
        throw new Error("UPDATE precisa receber um objeto.");
    }

    const baseSegments = normalizePath(databasePath);

    const transaction = db.transaction(() => {
        let root = getRoot();

        for (const [key, value] of Object.entries(values)) {
            const relativeSegments = normalizePath(key);
            const fullPath = [...baseSegments, ...relativeSegments];

            root = setAtPath(root, fullPath, value);
        }

        saveRoot(root);
    });

    transaction();
}

function remove(databasePath = "") {
    set(databasePath, null);
}

function compareAndSet(databasePath, expectedValue, newValue) {
    const segments = normalizePath(databasePath);

    const transaction = db.transaction(() => {
        const root = getRoot();
        const currentValue = getAtPath(root, segments);

        if (JSON.stringify(currentValue) !== JSON.stringify(expectedValue)) {
            return false;
        }

        const updatedRoot = setAtPath(root, segments, newValue);

        saveRoot(updatedRoot);

        return true;
    });

    return transaction();
}

module.exports = {
    get,
    set,
    update,
    remove,
    compareAndSet,
    normalizePath,
    close: () => db.close()
};
