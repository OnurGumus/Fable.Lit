/// Reading a `Fable.Store` from a component.
///
/// Written for this fork rather than taken from `Fable.LitStore`, which is the package
/// that would otherwise do this: that one depends on upstream `Fable.Lit`, and both
/// libraries live in the `Lit` namespace, so an app cannot reference it and this fork at
/// once. It ships no licence either, so the thirty lines below are a fresh implementation
/// of the same idea and the same signature -- `Hook.useStore store` -- so that moving
/// between the two is a package reference and nothing else.
module LitStore

open System
open Fable
open Lit

type HookContext with

    /// <summary>
    /// Renders with the store's current value, and again whenever it changes.
    /// </summary>
    /// <remarks>
    /// The subscription is taken in the initialiser rather than on every render, and is
    /// disposed when the component disconnects.
    ///
    /// It has to be taken before the setter it feeds exists -- `useState` cannot hand
    /// back a setter until it has a first value, and the first value is what the
    /// subscription reports -- so the callback goes through a ref that is pointed at the
    /// real setter immediately afterwards. Every later render points it at that render's
    /// setter, which is what keeps the store from writing into a stale closure.
    ///
    /// Disconnecting is not the end of a component. An element that is moved rather than
    /// removed is disconnected and connected again with its state intact, which is why
    /// `runEffects` re-runs `useEffectOnce` on reconnection -- so the effect has to be
    /// able to *make* a subscription, not hand back one it captured, or the second life
    /// of the component is spent showing the value the first one ended on.
    ///
    /// `subscribeImmediate` reports the current value as it subscribes, so the catching
    /// up happens by itself: whatever the store did while this was out of the document
    /// arrives in the same call that starts listening again.
    /// </remarks>
    member ctx.useStore(store: IStore<'Model>) : 'Model =
        let sink = ctx.useRef (fun () -> fun (_: 'Model) -> ())
        let held = ctx.useRef (fun () -> Unchecked.defaultof<IDisposable>)

        let subscribe () = store |> Store.subscribeImmediate (fun value -> sink.Value value)

        let model, setModel =
            ctx.useState (fun () ->
                let initial, disposable = subscribe ()

                held.Value <- disposable
                initial)

        sink.Value <- setModel

        ctx.useEffectOnce (fun () ->
            // First run: the initialiser's subscription is still live and this only has
            // to arrange for its disposal. Reconnection: it went on the way out, and a
            // new one is taken here.
            if isNull (box held.Value) then
                let current, disposable = subscribe ()
                held.Value <- disposable

                // And the catching up is this line, not the subscription. Subscribing
                // reports the current value by *returning* it -- the callback is only
                // told from the second notification on -- so a reconnection that only
                // resubscribed would listen correctly from now on while still showing
                // whatever was true when it left.
                sink.Value current

            { new IDisposable with
                member _.Dispose() =
                    held.Value.Dispose()
                    held.Value <- Unchecked.defaultof<IDisposable> })

        model

type Hook with

    /// <summary>
    /// Renders with the store's current value, and again whenever it changes.
    /// </summary>
    /// <example>
    ///     [&lt;HookComponent>]
    ///     let themeBadge () =
    ///         let theme = Hook.useStore ThemeStore.store
    ///         html $"""&lt;span>{Theme.name theme}&lt;/span>"""
    /// </example>
    static member inline useStore<'Model>(store: IStore<'Model>) : 'Model =
        Hook.getContext().useStore<'Model> (store)
