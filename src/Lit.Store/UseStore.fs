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
    /// The subscription is taken once, inside the initialiser, rather than on every
    /// render, and is disposed when the component disconnects.
    ///
    /// It has to be taken before the setter it feeds exists -- `useState` cannot hand
    /// back a setter until it has a first value, and the first value is what the
    /// subscription reports -- so the callback goes through a ref that is pointed at the
    /// real setter immediately afterwards. Every later render points it at that render's
    /// setter, which is what keeps the store from writing into a stale closure.
    /// </remarks>
    member ctx.useStore(store: IStore<'Model>) : 'Model =
        let sink = ctx.useRef (fun () -> fun (_: 'Model) -> ())
        let subscription = ctx.useRef (fun () -> Hook.emptyDisposable)

        let model, setModel =
            ctx.useState (fun () ->
                let initial, disposable = store |> Store.subscribeImmediate (fun value -> sink.Value value)

                subscription.Value <- disposable
                initial)

        sink.Value <- setModel
        ctx.useEffectOnce (fun () -> subscription.Value)
        model

type Hook with

    /// <summary>
    /// Renders with the store's current value, and again whenever it changes.
    /// </summary>
    /// <example>
    ///     [&lt;HookComponent>]
    ///     let bays () =
    ///         let session = Hook.useStore Session.store
    ///         html $"""&lt;p>{session.Reserved} reserved&lt;/p>"""
    /// </example>
    static member inline useStore<'Model>(store: IStore<'Model>) : 'Model =
        Hook.getContext().useStore<'Model> (store)
