mergeInto(LibraryManager.library, {
  // 1 while the page has focus and the cursor is over the game canvas (0 after it leaves or the tab/window loses focus).
  FwPointerInPage: function () {
    if (!window.fwPointer) {
      var st = { inside: false };
      window.fwPointer = st;
      var c = (typeof Module !== 'undefined' && Module['canvas']) || document.querySelector('canvas');
      var t = c || document.documentElement;
      t.addEventListener('mouseenter', function () { st.inside = true; });
      t.addEventListener('mousemove', function () { st.inside = true; });
      t.addEventListener('mouseleave', function () { st.inside = false; });
      document.addEventListener('mouseleave', function () { st.inside = false; });
      window.addEventListener('blur', function () { st.inside = false; });
    }
    return (window.fwPointer.inside && document.hasFocus() && !document.hidden) ? 1 : 0;
  }
});
