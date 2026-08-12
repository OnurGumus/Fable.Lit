namespace Lit.Elmish

open System
open Browser
open Browser.Types
open Elmish
open Lit
open Lit.HMRTypes

/// What a mounted program leaves behind on the element it renders into.
///
/// A hot update re-executes the module that started the program, and the module starts
/// it again: a second program on an element the first one is still driving. The element
/// is the one place the two can meet. A table held in the module is replaced along with
/// the module, and the bundler's own hot-update data is only there when the bundler is
/// the one doing the reloading, which is not the only way a module gets run twice.
module private Mount =

    type State() =
        /// True once lit owns this container's DOM. A later mount renders into it
        /// rather than trying to adopt markup that stopped being the server's.
        member val Rendered = false with get, set

        /// Stops the program mounted here, if any: its subscriptions are stopped and
        /// its dispatch loop stops accepting messages.
        member val Stop: (unit -> unit) option = None with get, set

        /// The last model rendered here. Development only, see `mountOn`.
        member val Model: obj = null with get, set

        member val HasModel = false with get, set

    [<Literal>]
    let private KEY = "__fableLitElmish"

    let stateOf (el: Element) : State = getOrAdd (box el) KEY State

[<RequireQualifiedAccess>]
module Program =
    /// Creates an elmish program without a view function.
    /// Useful for testing or using the program with `Hook.useElmish`.
    let mkHidden init update =
        let view _ _ = ()
        Program.mkProgram init update view

    /// Mounts a program on an element, taking over from whatever was mounted there
    /// before it.
    ///
    /// Nothing here asks how the module came to be running twice. A bundler's hot update
    /// is the usual answer, and Vite and webpack disagree about how to say so; a page
    /// that re-imports its own bundle after a rebuild is another, and says nothing at
    /// all. All three arrive here as a second call on an element that already has a
    /// program, which is the only fact this needs.
    let private mountOn (adopt: bool) (el: Element) (program: Program<'arg, 'model, 'msg, Lit.TemplateResult>): Program<'arg, 'model, 'msg, Lit.TemplateResult> =
        let state = Mount.stateOf el

        // A message no user code can produce, recognised by reference. It is created per
        // mount so that the closure sending it and the predicate recognising it always
        // come from one version of this module, which the two sides of a hot update do
        // not: a value held at module level would be replaced along with the module, and
        // the new one would not be the one the old program is watching for.
        let signal = obj ()

        // Stop the program the previous mount left running here. Elmish's own
        // termination path stops its subscriptions and closes its dispatch loop, so it
        // cannot go on reacting to a timer or a socket, and cannot render over the
        // program replacing it. Without this every hot update leaves another live loop
        // behind, and they are only invisible because nothing dispatches to them yet.
        state.Stop |> Option.iter (fun stop -> stop ())
        state.Stop <- None

        // Carrying the model over is the point of a hot update: the counter you are
        // looking at keeps its value while the code rendering it changes underneath.
        //
        // Development only. The model comes from code that no longer exists, and nothing
        // checks that the new code expects the same shape -- add a field and the restored
        // model is missing it. That is a fair trade while you are editing the file and a
        // crash nobody could explain in anything you shipped.
        let inherited: 'model option =
#if DEBUG
            if state.HasModel then Some(unbox<'model> state.Model) else None
#else
            None
#endif

        let setState model dispatch =
#if DEBUG
            state.Model <- box model
            state.HasModel <- true
#endif

            if state.Stop.IsNone then
                state.Stop <- Some(fun () -> dispatch (unbox<'msg> signal))

            let view = Program.view program model dispatch

            if state.Rendered then
                Lit.render el view
            else
                state.Rendered <- true
                if adopt then Hydrate.adopt el view else Lit.render el view

        program
        |> Program.map
            // An inherited model replaces init, and drops the command that came with it:
            // init's command belongs to a program starting, and this one is continuing.
            (fun init arg ->
                match inherited with
                | Some model -> model, Cmd.none
                | None -> init arg)
            id
            id
            (fun _ -> setState)
            id
            (fun (shouldTerminate, terminate) ->
                (fun msg -> obj.ReferenceEquals(box msg, signal) || shouldTerminate msg), terminate)

    /// <summary>
    /// Mounts an Elmish loop in the specified element
    /// </summary>
    /// <remarks>
    /// Mounting a second program on the same element stops the first one. In a
    /// development build the second also starts from the model the first had reached,
    /// so a hot update keeps the state on screen.
    /// </remarks>
    let withLitOnElement (el: Element) (program: Program<'arg, 'model, 'msg, Lit.TemplateResult>): Program<'arg, 'model, 'msg, Lit.TemplateResult> =
        mountOn false el program

    /// <summary>
    /// Mounts an Elmish loop in the specified element, adopting server-rendered markup
    /// on the first render instead of replacing it.
    /// </summary>
    /// <remarks>
    /// The element must hold markup produced by Lit.Server's hydratable rendering, and
    /// the model behind it must be the one the server rendered from. An Elmish program
    /// whose init is deterministic satisfies that by itself; one whose init depends on
    /// server state has to be given the same state, usually by embedding it in the page.
    ///
    /// Only the first render adopts. Everything after it is an ordinary render into DOM
    /// lit owns by then -- including the first render of a program mounted here later,
    /// after a hot update, which finds lit's DOM rather than the server's.
    /// </remarks>
    let withLitHydratedOnElement (el: Element) (program: Program<'arg, 'model, 'msg, Lit.TemplateResult>): Program<'arg, 'model, 'msg, Lit.TemplateResult> =
        mountOn true el program

    /// <summary>
    /// Mounts an Elmish loop in the element with the specified id, adopting
    /// server-rendered markup on the first render.
    /// </summary>
    let withLitHydrated (id: string) (program: Program<'arg, 'model, 'msg, Lit.TemplateResult>): Program<'arg, 'model, 'msg, Lit.TemplateResult> =
        let el = document.getElementById (id)

        if isNull el then
            failwith $"Cannot find element with id {id}"

        withLitHydratedOnElement el program

    /// <summary>
    /// Mounts an Elmish loop in the element with the specified id
    /// </summary>
    /// <remarks>
    /// The string passed must be an id of an element in the DOM, this function uses `document.getElementById(id)` to find the element.
    /// </remarks>
    let withLit (id: string) (program: Program<'arg, 'model, 'msg, Lit.TemplateResult>): Program<'arg, 'model, 'msg, Lit.TemplateResult> =
        let el = document.getElementById(id)
        if isNull el then
            failwith $"Cannot find element with id {id}"

        withLitOnElement el program

[<AutoOpen>]
module LitElmishExtensions =
    type ElmishObservable<'State, 'Msg>() =
        let mutable state: 'State option = None
        let mutable listener: ('State -> unit) option = None
        let mutable dispatcher: ('Msg -> unit) option = None

        member _.Value = state

        member _.SetState (model: 'State) (dispatch: 'Msg -> unit) =
            state <- Some model
            dispatcher <- Some dispatch
            match listener with
            | None -> ()
            | Some listener -> listener model

        member _.Dispatch(msg) =
            match dispatcher with
            | None -> () // Error?
            | Some dispatch -> dispatch msg

        member _.Subscribe(f) =
            match listener with
            | Some _ -> ()
            | None -> listener <- Some f

    let useElmish(ctx: HookContext, program: unit -> Program<unit, 'State, 'Msg, unit>) =
        let obs = ctx.useMemo(fun () -> ElmishObservable())

        let state, setState = ctx.useState(fun () ->
            program()
            |> Program.withSetState obs.SetState
            |> Program.run

            match obs.Value with
            | None -> failwith "Elmish program has not initialized"
            | Some v -> v)

        ctx.useEffectOnce(fun () ->
            Hook.createDisposable(fun () ->
                match box state with
                | :? System.IDisposable as disp -> disp.Dispose()
                | _ -> ()))

        obs.Subscribe(setState)
        state, obs.Dispatch

    type Hook with
        /// <summary>
        /// Start an [Elmish](https://elmish.github.io/elmish/) model-view-update loop.
        /// </summary>
        /// <example>
        ///      type State = { counter: int }
        ///
        ///      type Msg = Increment | Decrement
        ///
        ///      let init () = { counter = 0 }
        ///
        ///      let update msg state =
        ///          match msg with
        ///          | Increment -&gt; { state with counter = state.counter + 1 }
        ///          | Decrement -&gt; { state with counter = state.counter - 1 }
        ///
        ///      [&lt;HookComponent>]
        ///      let app () =
        ///          let state, dispatch = Hook.useElmish(init, update)
        ///         html $"""
        ///               &lt;header>Click the counter&lt;/header>
        ///               &lt;div id="count">{state.counter}&lt;/div>
        ///               &lt;button type="button" @click=${fun _ -> dispatch Increment}>
        ///                 Increment
        ///               &lt;/button>
        ///               &lt;button type="button" @click=${fun _ -> dispatch Decrement}>
        ///                   Decrement
        ///                &lt;/button>
        ///              """
        /// </example>
        static member inline useElmish(init: unit -> ('State * Cmd<'Msg>), update: 'Msg -> 'State -> ('State * Cmd<'Msg>)): 'State * ('Msg -> unit) =
            useElmish(Hook.getContext(), fun () -> Program.mkHidden init update)

        static member inline useElmish(program: Program<unit, 'State, 'Msg, unit>): 'State * ('Msg -> unit) =
            useElmish(Hook.getContext(), fun () -> program)

        static member inline useElmish(program: unit -> Program<unit, 'State, 'Msg, unit>): 'State * ('Msg -> unit) =
            useElmish(Hook.getContext(), program)
