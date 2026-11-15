mergeInto(LibraryManager.library, {
  // Flushes the Emscripten IndexedDB mount (persistentDataPath) so saves survive a page reload.
  MagicCampus_SyncFs: function () {
    try {
      FS.syncfs(false, function (err) {
        if (err) console.warn('MagicCampus save sync failed', err);
      });
    } catch (e) {
      console.warn('MagicCampus save sync unavailable', e);
    }
  }
});
