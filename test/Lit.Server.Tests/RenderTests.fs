/// What the .NET renderer must get right.
///
/// These are written against the behaviour lit has in a browser, not against whatever
/// this implementation happens to do: a boolean attribute is present or absent, a text
/// binding becomes a text node and so can never be markup, and a listener has no HTML
/// form and leaves with its value.
module Lit.Server.Tests.Render

open Xunit
open Lit

[<Fact>]
let ``a text hole is escaped`` () =
    // The one that is not cosmetic. lit builds a text node, so a value can never be
    // parsed as HTML on the client; a server that emitted it raw would be a hole in the
    // site rather than a rendering difference.
    let name = "<script>alert('x')</script>"
    Assert.Equal("<p>&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt;</p>", render (html $"<p>{name}</p>"))

[<Fact>]
let ``an attribute hole is escaped and quoted`` () =
    let title = "a \"quoted\" title"
    Assert.Equal("""<i title="a &quot;quoted&quot; title"></i>""", render (html $"<i title={title}></i>"))

[<Fact>]
let ``a set boolean attribute is bare`` () =
    Assert.Equal("<button disabled></button>", render (html $"<button ?disabled={true}></button>"))

[<Fact>]
let ``an unset boolean attribute is absent`` () =
    Assert.Equal("<button></button>", render (html $"<button ?disabled={false}></button>"))

[<Fact>]
let ``an event handler leaves with its binding`` () =
    // Not merely the value: the `@click=` has to go too, or the browser is served a
    // literal attribute that means nothing.
    Assert.Equal(
        """<button class="btn">Go</button>""",
        render (html $"""<button class="btn" @click={Ev(ignore)}>Go</button>""")
    )

[<Fact>]
let ``a property binding leaves with its binding`` () =
    let value = "hello"
    Assert.Equal("<input>", render (html $"<input .value={value}>"))

[<Fact>]
let ``a nested template is rendered in place`` () =
    let inner = html $"<b>{42}</b>"
    Assert.Equal("<p><b>42</b></p>", render (html $"<p>{inner}</p>"))

[<Fact>]
let ``a list of templates renders in order`` () =
    let rows = [ 1; 2; 3 ] |> List.map (fun n -> html $"<li>{n}</li>")
    Assert.Equal("<ul><li>1</li><li>2</li><li>3</li></ul>", render (html $"<ul>{Lit.ofList rows}</ul>"))

[<Fact>]
let ``nothing renders nothing`` () =
    Assert.Equal("<p></p>", render (html $"<p>{Lit.nothing}</p>"))

[<Fact>]
let ``classes joins the flags that are set`` () =
    let cls = Lit.classes [ "panel-size", true; "on", false; "wide", true ]
    Assert.Equal("""<div class="panel-size wide"></div>""", render (html $"<div class={cls}></div>"))

[<Fact>]
let ``literal braces survive`` () =
    // `{{` is how an interpolated string writes a brace, and inline CSS is full of them.
    Assert.Equal("<style>.a { color: red }</style>", render (html $"<style>.a {{ color: red }}</style>"))

[<Fact>]
let ``a boolean attribute given a non-bool is refused`` () =
    // Refusing loudly is the whole policy: a renderer that guessed here would ship
    // markup no test would catch.
    let notABool = "yes"

    Assert.Throws<UnsupportedTemplateValue>(fun () ->
        render (html $"<button ?disabled={notABool}></button>") |> ignore)

[<Fact>]
let ``a template composes as a Node`` () =
    // The point of returning HtmlTypeProvider's Node rather than a string: a rendered
    // island drops into a page template's hole with no string handling on either side.
    let sb = System.Text.StringBuilder()
    let greeting = "hi"
    (toNode (html $"<span>{greeting}</span>")).Invoke(sb)
    Assert.Equal("<span>hi</span>", sb.ToString())

// ---- bug hunt ----

[<Fact>]
let ``an equals sign in text is not an attribute binding`` () =
    // The classifier reads the tail of the literal before a hole. Prose can end in
    // `word = ` just as an attribute can, and only the parse state tells them apart.
    let n = 5
    Assert.Equal("<p>total = 5</p>", render (html $"<p>total = {n}</p>"))

[<Fact>]
let ``an element after a raw text element still counts`` () =
    // </style> ends raw text. If the scanner re-enters it, everything after the style
    // block is invisible: nodes go uncounted and bindings look like they are inside it.
    let cls = "wide"
    let t = html $"<div><style>x</style><b class={cls}>y</b></div>"
    // div 0, style 1, b 2
    Assert.Equal<int[]>([| 2 |], attributeElementIndices t)

[<Fact>]
let ``a hole in a quoted attribute value renders inside the quotes`` () =
    let name = "crate"
    Assert.Equal("""<i title="a crate"></i>""", render (html $"""<i title="a {name}"></i>"""))
