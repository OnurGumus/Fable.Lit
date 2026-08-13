/// Can markup rendered on .NET be adopted inside a shadow root?
///
/// The question matters because a declarative shadow root is the only way a component's
/// own DOM arrives with the page rather than after it, and because everything about
/// hydration so far has happened in the light DOM. Nothing in the marker protocol
/// mentions either: lit finds its bindings by walking comment nodes, and comments
/// survive being parsed into a shadow root exactly as they survive anything else. This
/// asks the browser whether that reasoning holds.
module ShadowTest

open Browser
open Fable.Core
open Fable.Core.JsInterop
open Lit
open WebTestRunner

[<Emit("fetch($0).then(r => r.ok ? r.json() : Promise.reject(new Error('missing ' + $0)))")>]
let private fetchJson (url: string) : JS.Promise<obj> = jsNative

/// lit's own hydrate, taken untyped: Fable.Lit's `Hydrate.adopt` asks for an Element,
/// and a shadow root is a DocumentFragment. Whether that signature should be widened is
/// the thing this test is here to decide.
[<Import("hydrate", "@lit-labs/ssr-client")>]
let private hydrate (value: obj) (container: obj) (options: obj) : unit = jsNative

/// innerHTML has not attached declarative shadow roots since Chrome 124: the templates
/// are left as templates, which would make this test pass by never testing anything.
[<Emit("$0.setHTMLUnsafe($1)")>]
let private setHTMLUnsafe (el: obj) (html: string) : unit = jsNative

describe "Declarative shadow DOM" <| fun () ->

    it "adopts server markup that arrived inside a shadow root" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"
        let markup: string = expected?("counter#hydratable")

        let host = document.createElement "div"
        document.body.appendChild host |> ignore

        setHTMLUnsafe host $"""<span id="card"><template shadowrootmode="open">{markup}</template></span>"""

        let card = host.querySelector "#card"
        let root: obj = card?shadowRoot

        if isNull root then
            failwith "the browser did not attach the declarative shadow root"

        let before: Browser.Types.HTMLElement = root?querySelector ("button")
        let shown () = (root?querySelector (".n"): Browser.Types.HTMLElement).textContent

        if shown () <> "0" then
            failwith $"the server's markup is not what reached the shadow root (shows {shown ()})"

        let mutable clicks = 0

        hydrate
            (box (SharedViews.counter { SharedViews.Count = 0 } (fun _ -> clicks <- clicks + 1)))
            root
            (box {| |})

        do! Promise.sleep 50

        if not (obj.ReferenceEquals(before, root?querySelector ("button"))) then
            failwith "hydrating replaced the shadow root's button instead of adopting it"

        before.click ()
        do! Promise.sleep 50

        if clicks <> 1 then
            failwith $"the click never reached the handler, so the event part was not created ({clicks})"

        document.body.removeChild host |> ignore
    }
