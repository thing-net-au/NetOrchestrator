window.logStream = {
    open: function (dotnetObject, url) {
        const source = new EventSource(url);
        source.onmessage = function (e) {
            // Call into the .NET component each time we get data
            dotnetObject.invokeMethodAsync('ReceiveLogChunk', e.data);
        };
        source.onerror = function (err) {
            console.error('Log stream error', err);
            source.close();
        };
        // Keep the JS EventSource alive
        return;
    },
    close: function (url) {
        // If you held onto sources by URL, you could close them here
    }
};
