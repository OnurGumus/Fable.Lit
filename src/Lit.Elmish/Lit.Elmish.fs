namespace Lit.Elmish

open System
open Browser
open Browser.Types
open Elmish
open Lit
open Fable.Core.JsInterop
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

        /// lit's root part for this container, once there is one, so the tree can be told
        /// it is disconnected when the program driving it stops.
        member val Part: Types.ChildPart option = None with get, set

        /// The last model rendered here. Development only, see `mountOn`.
        member val Model: obj = null with get, set

        member val HasModel = false with get, set

    [<Literal>]
    let private KEY = "__fableLitElmish"

    /// Keyed on the container, which is not always an element: markup that arrived as
    /// a declarative shadow root is adopted on the root itself.
    let stateOf (container: obj) : State = getOrAdd container KEY State

/// A model carried over a hot update, rebuilt in the new code's types.
///
/// The model a program had reached was made by the previous version of the module, and
/// equality in Fable asks more than whether two values look alike: a union case or a
/// record equals only one made by the same constructor. After a hot update every union
/// and record in the carried-over model has the old version's constructor, so the new
/// code's `card.Lane = Backlog` is false for every card, and nothing says why. So the
/// model is made again, piece by piece, with the new version's constructors: union cases
/// matched by name, record fields by name. A piece that does not fit -- a field the old
/// model never had, a case the new code no longer has -- means the new code expects
/// another shape, and the program starts from init instead.
///
/// Immutable F# data is rebuilt: records, unions, tuples, options, lists, arrays, maps
/// and sets. Anything else, a class or a mutable collection, is carried over as it is.
module private Rebuild =
    open FSharp.Reflection

    exception DoesNotFit of string

    let private isA (definition: Type) (t: Type) =
        t.IsGenericType && t.GetGenericTypeDefinition() = definition

    /// Whether a value of this type can hold anything made by a constructor.
    let rec private holdsData (t: Type) =
        t.IsArray
        || FSharpType.IsRecord t
        || FSharpType.IsUnion t
        || FSharpType.IsTuple t
        || (t.IsGenericType && t.GetGenericArguments() |> Array.exists holdsData)

    let rec private rebuild (path: string) (t: Type) (value: obj) : obj =
        if isNull value || not (holdsData t) then
            value
        // Options and lists are unions to Fable's reflection, and neither is made like one.
        elif isA typedefof<option<_>> t then
            match unbox<obj option> value with
            | Some inner -> box (Some(rebuild path (t.GetGenericArguments().[0]) inner))
            | None -> value
        elif isA typedefof<list<_>> t then
            let item = t.GetGenericArguments().[0]
            unbox<obj list> value |> List.mapi (fun i v -> rebuild $"{path}[{i}]" item v) |> box
        elif isA typedefof<Map<_, _>> t then
            let types = t.GetGenericArguments()

            // Keys and items are comparable by definition, so they are taken as such.
            unbox<Map<IComparable, obj>> value
            |> Map.toList
            |> List.map (fun (k, v) ->
                unbox<IComparable> (rebuild $"{path} key" types.[0] k), rebuild $"{path}[{k}]" types.[1] v)
            |> Map.ofList
            |> box
        elif isA typedefof<Set<_>> t then
            let item = t.GetGenericArguments().[0]
            unbox<Set<IComparable>> value
            |> Seq.map (fun v -> unbox<IComparable> (rebuild $"{path} item" item v))
            |> Set.ofSeq
            |> box
        elif t.IsArray then
            let item = t.GetElementType()
            unbox<obj[]> value |> Array.mapi (fun i v -> rebuild $"{path}[{i}]" item v) |> box
        elif FSharpType.IsTuple t then
            let items = FSharpType.GetTupleElements t

            let values =
                FSharpValue.GetTupleFields value
                |> Array.mapi (fun i v -> rebuild $"{path}.Item{i + 1}" items.[i] v)

            FSharpValue.MakeTuple(values, t)
        elif jsTypeof value <> "object" then
            // An erased union or a string enum: a plain value, made by no constructor.
            value
        elif FSharpType.IsRecord t then
            let values =
                FSharpType.GetRecordFields t
                |> Array.map (fun field ->
                    // An anonymous record leaves out an option field that is None.
                    if not (isIn field.Name value || isA typedefof<option<_>> field.PropertyType) then
                        raise (DoesNotFit $"{path} has no field {field.Name}")

                    rebuild $"{path}.{field.Name}" field.PropertyType (value?(field.Name)))

            FSharpValue.MakeRecord(t, values)
        elif FSharpType.IsUnion t then
            let name: string = value?name
            let fields: obj[] = value?fields

            match FSharpType.GetUnionCases t |> Array.tryFind (fun case -> case.Name = name) with
            | Some case when not (isNull fields) && case.GetFields().Length = fields.Length ->
                let types = case.GetFields()

                fields
                |> Array.mapi (fun i v -> rebuild $"{path}.{types.[i].Name}" types.[i].PropertyType v)
                |> fun values -> FSharpValue.MakeUnion(case, values)
            | Some _ -> raise (DoesNotFit $"{path} is {name} with different fields")
            | None -> raise (DoesNotFit $"{path} is {name}, which is no longer a case")
        else
            value

    /// The carried-over model in the new code's types, or None when it does not fit
    /// them. No type means none was known where the program was mounted, and the model
    /// is carried over as it is, as it always was.
    let model<'model> (modelType: Type) (previous: obj) : 'model option =
        if isNull modelType then
            Some(unbox<'model> previous)
        else
            try
                Some(unbox<'model> (rebuild "model" modelType previous))
            with
            | DoesNotFit reason ->
                console.warn $"The model on screen does not fit the new code ({reason}); starting from init."
                None
            | error ->
                console.warn ("The model on screen could not be rebuilt in the new code's types; carrying it over as it is.", error)
                Some(unbox<'model> previous)

/// Not for use from application code. Public only because the Program functions that
/// mount are inline, which they are so as to know the model's type where they are called.
[<System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)>]
module ProgramMount =
    /// The model's type, for rebuilding a carried-over model, in a development build;
    /// nothing in any other, which never carries a model over.
    let inline modelType<'model> () : Type =
#if DEBUG
        typeof<'model>
#else
        null
#endif

    /// Mounts a program on an element, taking over from whatever was mounted there
    /// before it.
    ///
    /// Nothing here asks how the module came to be running twice. A bundler's hot update
    /// is the usual answer, and Vite and webpack disagree about how to say so; a page
    /// that re-imports its own bundle after a rebuild is another, and says nothing at
    /// all. All three arrive here as a second call on an element that already has a
    /// program, which is the only fact this needs.
    let mountOn (adopt: bool) (modelType: Type) (el: #Node) (program: Program<'arg, 'model, 'msg, Lit.TemplateResult>): Program<'arg, 'model, 'msg, Lit.TemplateResult> =
        let state = Mount.stateOf (box el)

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
        // Development only. The model comes from code that no longer exists, so it is
        // rebuilt in the new code's types, and dropped for init when it no longer fits
        // them (see Rebuild). That is a fair trade while you are editing the file and
        // nothing anyone should rely on in what they ship.
        let inherited: 'model option =
#if DEBUG
            if state.HasModel then Rebuild.model<'model> modelType state.Model else None
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
                state.Part <- Some(Lit.renderPart el view)
            else
                state.Rendered <- true

                if adopt then
                    Hydrate.adopt el view

                    // Adoption goes through lit's hydrate, which stores the root part on
                    // the container rather than returning it. Same field lit's own render
                    // reads, and one it keeps unminified for exactly this reason.
                    state.Part <- Some(unbox<Types.ChildPart> ((box el)?("_$litPart$")))
                else
                    state.Part <- Some(Lit.renderPart el view)

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

    let private byId (id: string) =
        let el = document.getElementById (id)

        if isNull el then
            failwith $"Cannot find element with id {id}"

        el

    let mountById (adopt: bool) (modelType: Type) (id: string) (program: Program<'arg, 'model, 'msg, Lit.TemplateResult>): Program<'arg, 'model, 'msg, Lit.TemplateResult> =
        mountOn adopt modelType (byId id) program

    let mountInShadowRoot (modelType: Type) (id: string) (program: Program<'arg, 'model, 'msg, Lit.TemplateResult>): Program<'arg, 'model, 'msg, Lit.TemplateResult> =
        let root: ShadowRoot = (byId id)?shadowRoot

        if isNull (box root) then
            failwith
                $"The element with id {id} has no shadow root. A declarative one arrives with the page as <template shadowrootmode=\"open\">, which is what Lit.Server's toShadowRootNode writes; note that assigning innerHTML in the browser does not attach one."

        mountOn true modelType root program

[<RequireQualifiedAccess>]
module Program =
    /// Creates an elmish program without a view function.
    /// Useful for testing or using the program with `Hook.useElmish`.
    let mkHidden init update =
        let view _ _ = ()
        Program.mkProgram init update view

    /// <summary>
    /// Mounts an Elmish loop in the specified element
    /// </summary>
    /// <remarks>
    /// Mounting a second program on the same element stops the first one. In a
    /// development build the second also starts from the model the first had reached,
    /// so a hot update keeps the state on screen.
    /// </remarks>
    let inline withLitOnElement (el: Element) (program: Program<'arg, 'model, 'msg, Lit.TemplateResult>): Program<'arg, 'model, 'msg, Lit.TemplateResult> =
        ProgramMount.mountOn false (ProgramMount.modelType<'model> ()) el program

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
    let inline withLitHydratedOnElement (el: Element) (program: Program<'arg, 'model, 'msg, Lit.TemplateResult>): Program<'arg, 'model, 'msg, Lit.TemplateResult> =
        ProgramMount.mountOn true (ProgramMount.modelType<'model> ()) el program

    /// <summary>
    /// Mounts an Elmish loop in the element with the specified id, adopting
    /// server-rendered markup on the first render.
    /// </summary>
    let inline withLitHydrated (id: string) (program: Program<'arg, 'model, 'msg, Lit.TemplateResult>): Program<'arg, 'model, 'msg, Lit.TemplateResult> =
        ProgramMount.mountById true (ProgramMount.modelType<'model> ()) id program

    /// <summary>
    /// Mounts an Elmish loop in the element with the specified id
    /// </summary>
    /// <remarks>
    /// The string passed must be an id of an element in the DOM, this function uses `document.getElementById(id)` to find the element.
    /// </remarks>
    let inline withLit (id: string) (program: Program<'arg, 'model, 'msg, Lit.TemplateResult>): Program<'arg, 'model, 'msg, Lit.TemplateResult> =
        ProgramMount.mountById false (ProgramMount.modelType<'model> ()) id program

    /// <summary>
    /// Stops the program mounted on this container, if there is one.
    /// </summary>
    /// <remarks>
    /// Its subscriptions are stopped and its dispatch loop closed, through Elmish's own
    /// termination, and the tree it rendered is told it is disconnected so that any
    /// AsyncDirective in it lets go of what it was holding.
    ///
    /// For islands, which are the case with no lifecycle of their own: a component knows
    /// when it leaves the page, an Elmish program rendered into a plain element does not,
    /// and goes on running until something says otherwise. This is that something.
    ///
    /// Mounting another program on the same container does this too, so it is only needed
    /// when nothing is taking over.
    /// </remarks>
    let stopOn (el: #Node) =
        let state = Mount.stateOf (box el)

        state.Stop |> Option.iter (fun stop -> stop ())
        state.Stop <- None
        state.Part |> Option.iter (fun part -> part.setConnected false)

    /// <summary>
    /// Stops the program mounted on the element with the specified id, if there is one.
    /// </summary>
    let stop (id: string) =
        let el = document.getElementById (id)

        if not (isNull el) then
            stopOn el

    /// <summary>
    /// Mounts an Elmish loop in the shadow root of the element with the specified id,
    /// adopting the server-rendered markup that arrived inside it.
    /// </summary>
    /// <remarks>
    /// For markup written by Lit.Server's <c>toShadowRootNode</c>, which emits a
    /// <c>&lt;template shadowrootmode="open"&gt;</c> the HTML parser attaches as it reads
    /// the page. The shadow root, not its host, is the container the markers were written
    /// into, and hydrating the host would leave lit looking for a root part it cannot see.
    ///
    /// Styles written into the root sit outside those markers, so they are neither
    /// adopted nor re-rendered: they stay exactly as the server wrote them.
    /// </remarks>
    let inline withLitHydratedInShadowRoot (id: string) (program: Program<'arg, 'model, 'msg, Lit.TemplateResult>): Program<'arg, 'model, 'msg, Lit.TemplateResult> =
        ProgramMount.mountInShadowRoot (ProgramMount.modelType<'model> ()) id program

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
