/// Writes what .NET makes of the shared views, for the browser to check against.
///
/// The comparison happens in the browser rather than here, and deliberately: only a
/// browser can say whether two pieces of markup mean the same thing. lit renders into
/// the DOM and litters it with its own marker comments, so a string comparison would be
/// measuring the markers. The browser parses this output into a second DOM and compares
/// the trees with isEqualNode, which is the question actually worth asking.
module Lit.Server.Tests.Differential

open System.IO
open System.Text.Json
open Xunit
open Lit

[<Fact>]
let ``render every shared case for the browser to compare`` () =
    // Both the markup and lit's digest of the template. The digest is what a
    // `<!--lit-part ...-->` marker carries and what hydrate() checks, so a port that is
    // one bit out is a hydration that throws in production. The browser checks it
    // against lit's own function.
    let rendered =
        SharedViews.cases
        |> List.collect (fun (name, template) ->
            [ name, render template
              name + "#digest", digest template ])
        |> dict

    // Repo root from the test binary: bin/Debug/net10.0 -> test/Lit.Server.Tests -> test
    let root = Path.Combine(__SOURCE_DIRECTORY__, "..")
    let target = Path.Combine(root, "server-rendered.json")
    File.WriteAllText(target, JsonSerializer.Serialize(rendered, JsonSerializerOptions(WriteIndented = true)))

    // A guard, so a silently empty file cannot pass as agreement in the browser.
    Assert.NotEmpty(SharedViews.cases)
    for KeyValue(name, html) in rendered do
        Assert.False(System.String.IsNullOrWhiteSpace html, $"{name} rendered nothing")
