window.logStream = {
  _logSource: null,
  _statusSource: null,

  // --- Log stream methods ---

  open: function (dotnetObj, url) {
    console.log("logStream.open", url);
    this.close();
    this._logSource = this._createEventSource(
      url,
      dotnetObj,
      'ReceiveLogChunk',
      'LogError',
      () => {
        console.log("logStream log connected");
      }
    );
  },

  // Alias so Blazor can call openLog()
  openLog: function (dotnetObj, url) {
    this.open(dotnetObj, url);
  },

  close: function () {
    console.log("logStream.close");
    this._closeSource(this._logSource);
    this._logSource = null;
  },

  // Alias so Blazor can call closeLog()
  closeLog: function () {
    this.close();
  },

  // --- Status stream methods ---

  openStatus: function (dotnetObj, url) {
    console.log("logStream.openStatus", url);
    this.closeStatus();
    this._statusSource = this._createEventSource(
      url,
      dotnetObj,
      'ReceiveStatus',
      'StatusError',
      () => dotnetObj.invokeMethodAsync('StatusOpened')
    );
  },

  closeStatus: function () {
    console.log("logStream.closeStatus");
    this._closeSource(this._statusSource);
    this._statusSource = null;
  },

  // --- Shared helpers ---

  _createEventSource: function (url, dotnetObj, onMessageMethod, onErrorMethod, onOpenCallback) {
    const source = new EventSource(url);

    source.onopen = () => {
      console.log(`logStream: connected to ${url}`);
      if (onOpenCallback) onOpenCallback();
    };

    source.onmessage = e => {
      console.log("logStream message", e.data);
      dotnetObj.invokeMethodAsync(onMessageMethod, e.data);
    };

    source.onerror = err => {
      console.error(`logStream ${onErrorMethod}`, err);
      dotnetObj.invokeMethodAsync(onErrorMethod, err?.message || 'Unknown error');
      source.close();
    };

    return source;
  },

  _closeSource: function (source) {
    if (source) {
      try { source.close(); }
      catch (e) { console.warn("Error closing EventSource", e); }
    }
  },

  // --- Optional reconnect methods ---

  reconnectLog: function (dotnetObj, url, delayMs = 2000) {
    console.log("logStream.reconnectLog");
    setTimeout(() => this.open(dotnetObj, url), delayMs);
  },

  reconnectStatus: function (dotnetObj, url, delayMs = 2000) {
    console.log("logStream.reconnectStatus");
    setTimeout(() => this.openStatus(dotnetObj, url), delayMs);
  }
};
