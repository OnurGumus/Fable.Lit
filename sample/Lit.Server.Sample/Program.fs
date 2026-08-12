/// Renders an island's view on the server and drops it into a page.
///
/// Two systems meet here and neither has to know about the other. `SharedViews` is the
/// same file the browser tests compile with Fable, written in ordinary Fable.Lit: an
/// `html $"..."` template with `@click` handlers, `?disabled`, `Lit.classes`. Rendered
/// by Lit.Server it produces a `Node`, and a `Node` is exactly what a hole in an
/// HtmlTypeProvider page template accepts, so the two compose with no strings in
/// between and nothing to escape twice.
///
/// Run it: dotnet run --project sample/Lit.Server.Sample
module Sample

open HtmlTypeProvider
open Lit

type Page = Template<"page.html">

[<EntryPoint>]
let main _ =
    let items: SharedViews.Item list =
        [ { Name = "Crate"; Qty = 40 }
          { Name = "Pallet"; Qty = 2 }
          // Rendered by the client as a text node, and so escaped here too. The point
          // of the differential test is that these two agree about that.
          { Name = "<script>alert('x')</script>"; Qty = 1 } ]

    let page =
        Page()
            .Title("Lit.Server")
            // `toNode`, not `render`: the view goes in as a Node, not as a string.
            .Content(toNode (SharedViews.grid items))
            .Render()

    printfn "%s" page
    0
