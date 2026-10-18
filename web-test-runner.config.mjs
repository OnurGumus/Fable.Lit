// Fable.Expect 1.1.1 doesn't run as-is when compiled with Fable 5, so the two
// offending lines are patched when served to the browser:
// - Fable 5 emits regexes with the `u` flag, which rejects the `\!` escape
// - `super.stack = ...` throws inside the Error subclass constructor
// Remove this plugin once a fixed Fable.Expect is published.
const patchFableExpect = {
    name: "patch-fable-expect",
    transform(context) {
        if (!context.path.includes("/fable_modules/Fable.Expect.")) return;
        if (context.path.endsWith("/WebTestRunner.fs.js")) {
            return { body: context.body.replace("<\\!--", "<!--") };
        }
        if (context.path.endsWith("/Expect.fs.js")) {
            return { body: context.body.replace('super.stack = "<stack hidden>";', "") };
        }
    },
};

export default {
    files: "build/test/*Test.js",
    nodeResolve: true,
    // Tests awaiting requestAnimationFrame time out when pages run in parallel
    concurrency: 1,
    plugins: [patchFableExpect],
};
