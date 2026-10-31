namespace Lit

open Fable.Core

/// Changes made inside a document view transition, so the page animates from how it
/// looked before the change to how it looks after.
///
/// The browser takes a picture before the change and another after it. The second is
/// taken once the change's own microtasks have run, and a component's render is one of
/// them, as is the render it hands a nested component: what a change draws through
/// lit is in the picture without any waiting here.
///
/// One transition at a time, for the whole page. A document runs one at a time and
/// starting another cuts the running one short, so a click during an animation would
/// end it early; queued, fast clicks animate one after another, in order.
///
/// While a transition runs, the root element carries `data-transition="<kind>"`, so a
/// page's CSS can name elements for this kind of change only. A browser without view
/// transitions, or a reader who asked for reduced motion, gets the change without the
/// animation and without the mark.
[<RequireQualifiedAccess>]
module ViewTransition =

    /// The queue, as the promise of the last transition asked for. A failure is reported
    /// and swallowed here, so the next one still runs.
    [<Emit("""(() => {
        let last = Promise.resolve();
        return (kind, change) => {
            last = last.then(async () => {
                if (!document.startViewTransition ||
                    matchMedia('(prefers-reduced-motion: reduce)').matches) {
                    change();
                    return;
                }
                document.documentElement.dataset.transition = kind;
                try {
                    const transition = document.startViewTransition(change);
                    // A skipped animation is not a failure, and a failed change is
                    // reported once, below, through `finished`, not again from here.
                    transition.ready.catch(() => {});
                    transition.updateCallbackDone.catch(() => {});
                    await transition.finished;
                } finally {
                    delete document.documentElement.dataset.transition;
                }
            }).catch(error => console.error('A change in a view transition failed:', error));
            return last;
        };
    })()""")>]
    let private createQueue () : obj = jsNative

    [<Emit("$0($1, $2)")>]
    let private enqueue (queue: obj) (kind: string) (change: unit -> unit) : JS.Promise<unit> = jsNative

    let private queue = createQueue ()

    /// Runs `change` inside a view transition marked `kind`, after any earlier one has
    /// finished. The promise settles when this one has, animated or not.
    let run (kind: string) (change: unit -> unit) : JS.Promise<unit> = enqueue queue kind change

    /// Sends `message` inside a view transition marked `kind`: `dispatch` from an Elmish
    /// program, or a component's own `send`, with the update animated.
    let dispatch (kind: string) (send: 'msg -> unit) (message: 'msg) : unit =
        run kind (fun () -> send message) |> ignore
