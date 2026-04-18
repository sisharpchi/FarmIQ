window.farmiqAuth = {
    saveSession: function (key, value) {
        sessionStorage.setItem(key, JSON.stringify(value));
    },
    loadSession: function (key) {
        const raw = sessionStorage.getItem(key);
        return raw ? JSON.parse(raw) : null;
    },
    clearSession: function (key) {
        sessionStorage.removeItem(key);
    }
};
