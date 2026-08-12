// The repository ran without one of these for four years, which is how it ended up
// depending on defaults that suit a developer laptop and not a CI runner.
export default {
  // The development build of lit, not the minified one. Two reasons, and the second is
  // the load-bearing one: dev builds carry lit's warnings, and the minified build
  // mangles the internal names on `_$LH` that lit's own SSR uses -- so a test that holds
  // our node indices against lit's own count can only ask lit for them here.
  nodeResolve: {
    exportConditions: ['development'],
  },

  // Thirty seconds is the default, and a GitHub runner misses it: two of the four
  // files reported "the browser was unable to create and start a test page after
  // 30000ms" while the other two passed, so nothing failed and half the suite simply
  // never ran. A cold runner starting several Chrome pages at once is slower than any
  // laptop, and the honest fix is to let it be slow rather than to call that a
  // failure.
  browserStartTimeout: 60000,
  testsStartTimeout: 60000,

  // Two at a time. The default is cores/2, which is 6 on the machine this was written
  // on and starves a 4-core runner of the memory to open that many Chrome pages.
  //
  // Note this is a resource limit, not a correctness one. The suite used to *need*
  // concurrency 1, because `elementUpdated` awaited an animation frame and Chrome
  // suspends those in a background tab; that is fixed at the source in Lit.Test.fs,
  // and the suite now passes at any concurrency. If it ever starts depending on this
  // number again, something has gone back to waiting on a frame.
  concurrency: 2,
};
