/// What happens when the module that started an Elmish program runs a second time.
///
/// That is what a hot update is: the bundler hands the page a new copy of a module and
/// the top-level `Program.run` in it runs again, on an element the previous copy is
/// still driving. Nothing in these tests involves a bundler, because nothing in the
/// implementation does either -- a second mount on the same element is the whole of it,
/// and a test can simply mount twice.
module ElmishTest

open System
open Browser
open Fable.Core
open Fable.Core.JsInterop
open Elmish
open Lit
open Lit.Elmish
open WebTestRunner

[<Emit("fetch($0).then(r => r.ok ? r.json() : Promise.reject(new Error('missing ' + $0)))")>]
let private fetchJson (url: string) : JS.Promise<obj> = jsNative

let private host () =
    let el = document.createElement "div"
    document.body.appendChild el |> ignore
    el

let private init () = { SharedViews.Count = 0 }, Cmd.none

let private update (msg: SharedViews.Msg) (model: SharedViews.Model) =
    match msg with
    | SharedViews.Increment -> { model with SharedViews.Count = model.Count + 1 }, Cmd.none

// The same types twice, as a module and the next version of it each define them. In
// Fable a union case or a record is equal only to one made by the same constructor, and
// these two copies have different ones, exactly like two versions of one module.
module private Before =
    type Lane =
        | Backlog
        | Shipped

    type Tag = Tag of string
    type Card = { Id: int; Lane: Lane }

    type Model =
        { Cards: Card list
          Current: Lane option
          Pair: Lane * int
          Lanes: Lane array
          ByLane: Map<Lane, int>
          Tag: Tag }

    type Small = { Count: int }

module private After =
    type Lane =
        | Backlog
        | Shipped

    type Tag = Tag of string
    type Card = { Id: int; Lane: Lane }

    type Model =
        { Cards: Card list
          Current: Lane option
          Pair: Lane * int
          Lanes: Lane array
          ByLane: Map<Lane, int>
          Tag: Tag }

    /// `Small` with a field the previous version did not have.
    type Small = { Count: int; Label: string }

/// Every place in the model a union or record is compared, answered by the new code.
let private board (model: After.Model) =
    let backlog = model.Cards |> List.filter (fun card -> card.Lane = After.Backlog) |> List.length

    [ string backlog
      string (model.Current = Some After.Shipped)
      string (fst model.Pair = After.Backlog)
      string (model.Lanes.[0] = After.Shipped)
      string (defaultArg (Map.tryFind After.Backlog model.ByLane) -1)
      string (model.Tag = After.Tag "t")
      string (model.Cards.[0] = { Id = 1; Lane = After.Backlog }) ]
    |> String.concat " "

let private boardView (model: After.Model) (_: unit -> unit) =
    html $"""<p class="n">{board model}</p>"""

describe "Elmish remount" <| fun () ->

    // The reason anyone wants a hot update rather than a reload: the state you are
    // looking at is usually the state you are working on, and reaching it again by hand
    // after every edit is most of what makes a reload expensive.
    it "carries the model over to a program mounted after it" <| fun () -> promise {
        let el = host ()

        Program.mkProgram init update SharedViews.counter
        |> Program.withLitOnElement el
        |> Program.run

        do! Promise.sleep 50
        (el.querySelector "button" :?> Browser.Types.HTMLElement).click ()
        do! Promise.sleep 50

        // The module running again: a second program, from the same init as the first.
        Program.mkProgram init update SharedViews.counter
        |> Program.withLitOnElement el
        |> Program.run

        do! Promise.sleep 50

        let shown = (el.querySelector ".n").textContent

        if shown <> "1" then
            failwith $"the second mount started from init instead of the model on screen (shows {shown})"

        document.body.removeChild el |> ignore
    }

    // A replaced program that keeps running is not idle. Its subscriptions still fire,
    // and every message it has already handed out still reaches its setState, which
    // renders into the element the new program now owns.
    it "stops the program it replaced" <| fun () -> promise {
        let el = host ()
        let mutable stopped = false
        let mutable firstDispatch: SharedViews.Msg -> unit = ignore

        let view model dispatch =
            firstDispatch <- dispatch
            SharedViews.counter model dispatch

        let subscribe _ : Sub<SharedViews.Msg> =
            [ [ "ticker" ],
              fun _ ->
                  { new IDisposable with
                      member _.Dispose() = stopped <- true } ]

        Program.mkProgram init update view
        |> Program.withSubscription subscribe
        |> Program.withLitOnElement el
        |> Program.run

        do! Promise.sleep 50

        if stopped then
            failwith "the subscription was stopped before anything replaced it"

        Program.mkProgram init update SharedViews.counter
        |> Program.withLitOnElement el
        |> Program.run

        do! Promise.sleep 50

        if not stopped then
            failwith "the replaced program's subscription is still running"

        let before = (el.querySelector ".n").textContent
        firstDispatch SharedViews.Increment
        do! Promise.sleep 50
        let after = (el.querySelector ".n").textContent

        if before <> after then
            failwith $"a message dispatched to the replaced program still reached the DOM ({before} to {after})"

        document.body.removeChild el |> ignore
    }

    // The line a Vite or webpack entry module adds to let the bundler drive this. A test
    // run has no dev server behind it, which is the case worth pinning down: `accept` is
    // reached through `import.meta.hot`, which is not there, and this sits on the first
    // line of an application's startup where a throw takes the whole page with it.
    it "accepting hot updates is harmless where nothing implements them" <| fun () -> promise { HMR.acceptSelf () }

    // Adoption is a one-time act, and the second mount is not the one it is for. lit
    // refuses outright to hydrate a container it is already rendering into -- "container
    // already contains a live render" -- so a mount that asked would fall back to
    // emptying the element and rebuilding it, which is a reload with extra steps.
    it "renders rather than adopts when it takes over DOM lit already owns" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"

        let el = host ()
        el.innerHTML <- (expected?("counter#hydratable"): string)

        let warnings = ResizeArray()
        let realWarn = console?warn
        console?warn <- fun (message: obj) -> warnings.Add message

        Program.mkProgram init update SharedViews.counter
        |> Program.withLitHydratedOnElement el
        |> Program.run

        do! Promise.sleep 50
        let adopted = el.querySelector "button"

        // The module running again, over DOM that belongs to lit now, not the server.
        Program.mkProgram init update SharedViews.counter
        |> Program.withLitHydratedOnElement el
        |> Program.run

        do! Promise.sleep 50
        console?warn <- realWarn

        if warnings.Count > 0 then
            failwith $"the second mount tried to adopt DOM lit already owned ({warnings.Count} warning(s))"

        if not (obj.ReferenceEquals(adopted, el.querySelector "button")) then
            failwith "the second mount rebuilt the element instead of patching it"

        document.body.removeChild el |> ignore
    }

describe "Elmish remount across versions" <| fun () ->

    // What a hot update hands the new program: a model made by the previous version of
    // the module. Left as it is, every comparison the new code makes against it is false
    // -- the board would show every card in no lane at all -- with no error to say why.
    it "rebuilds the model it carries over in the new code's types" <| fun () -> promise {
        let el = host ()

        let previous: Before.Model =
            { Cards = [ { Id = 1; Lane = Before.Backlog }; { Id = 2; Lane = Before.Shipped }; { Id = 3; Lane = Before.Backlog } ]
              Current = Some Before.Shipped
              Pair = Before.Backlog, 4
              Lanes = [| Before.Shipped |]
              ByLane = Map.ofList [ Before.Backlog, 2 ]
              Tag = Before.Tag "t" }

        // The previous version's program, on the model it had reached.
        Program.mkProgram (fun () -> unbox<After.Model> previous, Cmd.none) (fun () model -> model, Cmd.none) boardView
        |> Program.withLitOnElement el
        |> Program.run

        do! Promise.sleep 50

        // The new version, whose own init would show something else entirely.
        let fresh: After.Model =
            { Cards = []
              Current = None
              Pair = After.Shipped, 0
              Lanes = [| After.Backlog |]
              ByLane = Map.empty
              Tag = After.Tag "fresh" }

        Program.mkProgram (fun () -> fresh, Cmd.none) (fun () model -> model, Cmd.none) boardView
        |> Program.withLitOnElement el
        |> Program.run

        do! Promise.sleep 50

        let shown = (el.querySelector ".n").textContent
        let expected = "2 true true true 2 true true"

        if shown <> expected then
            failwith $"the carried-over model does not compare equal to the new code's values (shows \"{shown}\", expected \"{expected}\")"

        document.body.removeChild el |> ignore
    }

    // A field the new code has and the old model does not: the restored model would be
    // missing it, and the first thing to read it would fail far from here. Starting from
    // init loses the state on screen, which is what a reload would have done anyway.
    it "starts from init when the model it would carry over does not fit the new code" <| fun () -> promise {
        let el = host ()

        let warnings = ResizeArray()
        let realWarn = console?warn
        console?warn <- fun (message: obj) -> warnings.Add message

        let view (model: After.Small) (_: unit -> unit) =
            html $"""<p class="n">{model.Count} {model.Label.ToUpper()}</p>"""

        Program.mkProgram (fun () -> unbox<After.Small> ({ Count = 5 }: Before.Small), Cmd.none) (fun () model -> model, Cmd.none) (fun model _ ->
            html $"""<p class="n">{model.Count}</p>""")
        |> Program.withLitOnElement el
        |> Program.run

        do! Promise.sleep 50

        Program.mkProgram (fun () -> ({ Count = 0; Label = "init" }: After.Small), Cmd.none) (fun () model -> model, Cmd.none) view
        |> Program.withLitOnElement el
        |> Program.run

        do! Promise.sleep 50
        console?warn <- realWarn

        let shown = (el.querySelector ".n").textContent

        if shown <> "0 INIT" then
            failwith $"the second mount did not start from init (shows \"{shown}\")"

        if warnings.Count <> 1 then
            failwith $"expected one warning saying why the model was dropped, got {warnings.Count}"

        document.body.removeChild el |> ignore
    }
