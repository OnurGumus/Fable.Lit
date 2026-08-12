/// Does the .NET renderer agree with lit?
///
/// The only honest way to ask is in a browser. `Lit.Server` produces a string and lit
/// produces a DOM, so the comparison parses the server's string into a DOM too and
/// compares what the browser makes of each. That normalises everything neither side
/// should be judged on: attribute quoting, the serialisation of a bare boolean
/// attribute, the order the parser hands things back.
///
/// lit's own marker comments are removed first. They are how lit finds its bindings
/// again on the next render, they are absent by design from server output, and leaving
/// them in would mean measuring them rather than the markup.
///
/// The expectations come from test/server-rendered.json, written by the .NET test
/// project from the same SharedViews.fs this file compiles. So `dotnet test` has to run
/// before `npm test`; if the file is missing, this fails rather than passes quietly.
module DifferentialTest

open Browser
open Fable.Core
open Fable.Core.JsInterop
open Lit
open Expect
open WebTestRunner

[<Emit("fetch($0).then(r => r.ok ? r.json() : Promise.reject(new Error('missing ' + $0)))")>]
let private fetchJson (url: string) : JS.Promise<obj> = jsNative

/// Comment nodes out, at every depth.
let rec private stripComments (node: Browser.Types.Node) =
    let children = [ for i in 0 .. int node.childNodes.length - 1 -> node.childNodes.[i] ]

    for child in children do
        if child.nodeType = 8.0 then
            node.removeChild child |> ignore
        else
            stripComments child

/// What lit makes of a template, with its bookkeeping removed.
let private fromLit (template: TemplateResult) =
    let host = document.createElement "div"
    document.body.appendChild host |> ignore
    Lit.render host template
    stripComments host
    let html = host.innerHTML
    document.body.removeChild host |> ignore
    html

/// What the browser makes of a piece of server markup.
///
/// Parsed inside a <template>, not a <div>, and the difference is not academic: the
/// HTML parser discards <tr> and <td> that are not inside a table, so a row rendered
/// into a div comes back as bare text. A template element parses fragments as written,
/// which is also how lit parses its own templates -- so this compares like with like.
let private fromServer (html: string) =
    let host = document.createElement "template" :?> Browser.Types.HTMLTemplateElement
    host.innerHTML <- html
    stripComments (host.content :> Browser.Types.Node)
    host.innerHTML

describe "Differential" <| fun () ->
    it "the .NET renderer agrees with lit on every shared view" <| fun () -> promise {
        let! expected = fetchJson "/test/server-rendered.json"

        for name, template in SharedViews.cases do
            let rendered = fromLit template
            let served = fromServer (expected?(name): string)

            // Named in the failure, or a mismatch in the seventh case reads as a
            // mismatch in nothing at all.
            if rendered <> served then
                failwith $"{name}\n  lit:    {rendered}\n  server: {served}"
    }
