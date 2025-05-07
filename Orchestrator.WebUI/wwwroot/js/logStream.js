window.logStream = (() => {
    // --- Log Stream Helper ---
    function createEventSource(url, dotnetObj, onMessageMethod, onErrorMethod, onOpenCallback) {
        const source = new EventSource(url);

        source.onopen = () => {
            console.log(`Connected to ${url}`);
            if (onOpenCallback) onOpenCallback();
        };

        source.onmessage = (e) => {
            console.log(`Message from ${url}`, e.data);
            dotnetObj.invokeMethodAsync(onMessageMethod, e.data);
        };

        source.onerror = (err) => {
            console.error(`Error from ${url}`, err);
            dotnetObj.invokeMethodAsync(onErrorMethod, err?.message || 'Unknown error');
            source.close();
        };

        return source;
    }

    function closeEventSource(source) {
        if (source) {
            try {
                source.close();
            } catch (e) {
                console.warn("Error closing EventSource", e);
            }
        }
    }

    // --- Service Log Stream ---
    const serviceLogStream = {
        _source: null,

        open: function (dotnetObj, url) {
            console.log("Service Log: Opening", url);
            this.close();
            this._source = createEventSource(
                url,
                dotnetObj,
                'ReceiveLogChunk',
                'LogError',
                () => console.log("Service Log: Connected")
            );
        },

        close: function () {
            console.log("Service Log: Closing");
            closeEventSource(this._source);
            this._source = null;
        },

        reconnect: function (dotnetObj, url, delayMs = 2000) {
            console.log("Service Log: Reconnecting");
            setTimeout(() => this.open(dotnetObj, url), delayMs);
        }
    };

    // --- Supervisor Log Stream ---
    const supervisorLogStream = {
        _source: null,

        open: function (dotnetObj, url) {
            console.log("Supervisor Log: Opening", url);
            this.close();
            this._source = createEventSource(
                url,
                dotnetObj,
                'ReceiveSupervisorLogChunk',
                'LogError',
                () => console.log("Supervisor Log: Connected")
            );
        },

        close: function () {
            console.log("Supervisor Log: Closing");
            closeEventSource(this._source);
            this._source = null;
        },

        reconnect: function (dotnetObj, url, delayMs = 2000) {
            console.log("Supervisor Log: Reconnecting");
            setTimeout(() => this.open(dotnetObj, url), delayMs);
        }
    };

    // --- Status Stream ---
    const statusStream = {
        _source: null,

        open: function (dotnetObj, url) {
            console.log("Status Stream: Opening", url);
            this.close();
            this._source = createEventSource(
                url,
                dotnetObj,
                'ReceiveStatus',
                'StatusError',
                () => dotnetObj.invokeMethodAsync('StatusOpened')
            );
        },

        close: function () {
            console.log("Status Stream: Closing");
            closeEventSource(this._source);
            this._source = null;
        },

        reconnect: function (dotnetObj, url, delayMs = 2000) {
            console.log("Status Stream: Reconnecting");
            setTimeout(() => this.open(dotnetObj, url), delayMs);
        }
    };

    // --- Public API ---
    return {
        serviceLog: serviceLogStream,
        supervisorLog: supervisorLogStream,
        status: statusStream
    };
})();
