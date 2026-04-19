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
    },
    saveValue: function (key, value) {
        sessionStorage.setItem(key, value);
    },
    loadValue: function (key) {
        return sessionStorage.getItem(key);
    },
    removeValue: function (key) {
        sessionStorage.removeItem(key);
    },
    setDocumentLanguage: function (value) {
        document.documentElement.lang = value || "en";
    }
};
