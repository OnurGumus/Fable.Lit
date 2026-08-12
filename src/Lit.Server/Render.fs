/// Renders Fable.Lit templates on .NET.
///
/// The same `html $"..."` view function compiles two ways: by Fable, against the real
/// bindings, into lit templates in the browser; and by the .NET compiler, against this,
/// into HTML. A project references one or the other, never both -- they declare the
/// same `Lit` namespace on purpose, so a shared view file keeps `open Lit` and needs no
/// conditional compilation.
///
/// Nothing here reimplements what HtmlTypeProvider already does. Escaping, the Node
/// model and the composition point are all its: a rendered template is a `Node`, which
/// is precisely what a page template's hole accepts, so an island's markup drops into a
/// server-rendered page without either side handling strings.
///
/// What this is not: it does not hydrate. lit's client-side `hydrate()` only adopts
/// markup produced by lit's own SSR, carrying marker comments this does not emit, so
/// the client renders over whatever the server sent. That is what the islands already
/// do with their hand-written skeletons; this only means the skeleton no longer has to
/// be written twice.
namespace Lit

open System
open System.Text
open System.Text.RegularExpressions
open HtmlTypeProvider

/// A template and the values that fill its holes. Under Fable this same source produces
/// lit's own TemplateResult instead.
type TemplateResult =
    internal
        { Segments: string[]
          Values: obj[] }

/// An event handler on its way into a template. The server drops it: a handler is a
/// closure over client state and there is nothing to serialise. Named to match the
/// Fable side so a shared view file type-checks against either.
type EvHandler = EvHandler of obj

/// Raised when a template uses something this renderer cannot honour.
///
/// Loudly, and never by guessing. A directive such as `styleMap` or `until` does its
/// work inside lit at commit time, and a renderer that quietly emitted an approximation
/// would ship subtly wrong markup that no test would catch. Refusing is the only honest
/// answer until each one is implemented deliberately.
exception UnsupportedTemplateValue of message: string

[<AutoOpen>]
module Server =

    /// A hole preceded by `name=`, `@name=`, `?name=` or `.name=`.
    ///
    /// This is the whole game. lit decides what a binding *is* from the literal text in
    /// front of the hole, and here that text is ours both to read and to rewrite: the
    /// sigil has to come out of the output, or the browser is served a literal
    /// `@click=""` attribute that means nothing to anyone.
    let private binding =
        Regex(@"(?<sigil>[@?.])?(?<name>[A-Za-z_:][-\w:.]*)\s*=\s*$", RegexOptions.Compiled)

    /// Rebuilds the literal segments from `FormattableString.Format`.
    ///
    /// Fable's FormattableString has `GetStrings()` and hands lit the segments directly;
    /// .NET's does not, so "a{0}b" is re-split here, honouring the `{{` and `}}` escapes
    /// that an interpolated string uses for literal braces.
    let internal splitFormat (format: string) =
        let segments = ResizeArray<string>()
        let current = StringBuilder()
        let mutable i = 0

        while i < format.Length do
            match format.[i] with
            | '{' when i + 1 < format.Length && format.[i + 1] = '{' ->
                current.Append('{') |> ignore
                i <- i + 2
            | '{' ->
                let close = format.IndexOf('}', i)

                if close < 0 then
                    failwithf "Unterminated hole in template: %s" format

                segments.Add(current.ToString())
                current.Clear() |> ignore
                i <- close + 1
            | '}' when i + 1 < format.Length && format.[i + 1] = '}' ->
                current.Append('}') |> ignore
                i <- i + 2
            | c ->
                current.Append(c) |> ignore
                i <- i + 1

        segments.Add(current.ToString())
        segments.ToArray()

    /// Interprets a template literal as HTML.
    let html (fmt: FormattableString) : TemplateResult =
        { Segments = splitFormat fmt.Format
          Values = fmt.GetArguments() }

    /// The text a value contributes in text position.
    ///
    /// Through `Node.Text`, so the encoding is HtmlTypeProvider's rather than a second
    /// opinion about it. This matters more than it looks: lit builds a *text node* for a
    /// text binding, so markup in a value can never be parsed as HTML on the client. A
    /// server renderer that emitted it raw would not be slightly different, it would be
    /// a hole in the site.
    let rec private textNode (value: obj) : Node =
        match value with
        | null -> Node.Empty()
        | :? EvHandler -> Node.Empty()
        | :? TemplateResult as t -> toNode t
        | :? seq<TemplateResult> as items -> items |> Seq.map toNode |> Node.Fragment
        | :? string as s -> Node.Text s
        | :? bool as b -> Node.Text(if b then "true" else "false")
        | v -> Node.Text(string v)

    /// The template as a `Node`, ready to compose into a page.
    and toNode (t: TemplateResult) : Node =
        let parts = ResizeArray<Node>()

        for i in 0 .. t.Segments.Length - 1 do
            let segment = t.Segments.[i]

            if i >= t.Values.Length then
                parts.Add(Node.RawHtml segment)
            else
                let value = t.Values.[i]
                let m = binding.Match(segment)

                if not m.Success then
                    parts.Add(Node.RawHtml segment)
                    parts.Add(textNode value)
                else
                    let lead = segment.Substring(0, m.Index)
                    let name = m.Groups.["name"].Value

                    match m.Groups.["sigil"].Value with
                    // A listener and a property have no HTML form at all, so the binding
                    // leaves with its value. TrimEnd, or dropping it strands the space
                    // that separated it from the attribute before.
                    | "@"
                    | "." -> parts.Add(Node.RawHtml(lead.TrimEnd()))

                    // A boolean attribute is present or absent. There is no `false`.
                    | "?" ->
                        match value with
                        | :? bool as on when on ->
                            // The space in front of it separated it from the attribute
                            // before, and is wanted.
                            parts.Add(Node.RawHtml lead)
                            parts.Add(Node.RawHtml name)
                        // Absent, and so is the space that led to it, or the element
                        // renders as `<button >`.
                        | :? bool -> parts.Add(Node.RawHtml(lead.TrimEnd()))
                        | v ->
                            parts.Add(Node.RawHtml lead)

                            raise (
                                UnsupportedTemplateValue
                                    $"?{name} expects a bool, got {v.GetType().Name}. In lit a boolean attribute is present or absent."
                            )

                    | _ ->
                        parts.Add(Node.RawHtml(lead + name + "=\""))

                        match value with
                        | null -> ()
                        | :? EvHandler ->
                            raise (UnsupportedTemplateValue $"An event handler cannot be an attribute value ({name}).")
                        | :? TemplateResult ->
                            raise (
                                UnsupportedTemplateValue
                                    $"A nested template cannot be an attribute value ({name})."
                            )
                        // Node.Text encodes quotes as well as angle brackets, so it is
                        // safe in an attribute, not only in text.
                        | :? string as s -> parts.Add(Node.Text s)
                        | :? bool as b -> parts.Add(Node.RawHtml(if b then "true" else "false"))
                        | v -> parts.Add(Node.Text(string v))

                        parts.Add(Node.RawHtml "\"")

        Node.Fragment parts

    /// The template as an HTML string.
    let render (t: TemplateResult) =
        let sb = StringBuilder()
        (toNode t).Invoke(sb)
        sb.ToString()

    // Below: the template-facing surface a shared view file needs in order to compile on
    // .NET. Each either does the same work as its Fable counterpart, or stands in for
    // something the server has no use for.

    /// Wrapper for event handlers to help type checking. Dropped when rendered.
    let inline Ev (handler: 'E -> unit) = EvHandler(box handler)

    /// The signatures below are not a design: they are copied from the Fable side, and
    /// have to stay copied. A shared view file compiles against whichever of the two is
    /// referenced, so any drift here is a file that builds in the browser and not on the
    /// server, or worse, builds on both and means different things.
    type Lit =
        /// Renders nothing. A TemplateResult like any other, because that is how it is
        /// typed on the Fable side: `if x then html $"..." else Lit.nothing` must compile.
        static member nothing: TemplateResult = { Segments = [| "" |]; Values = [||] }

        /// Joins the classes whose flag is set. Pure string work on both sides, so this
        /// one is genuinely the same function rather than a stand-in.
        static member classes(classes: (string * bool) seq) =
            classes |> Seq.filter snd |> Seq.map fst |> String.concat " "

        static member classes(classes: string seq) = classes |> String.concat " "

        /// Renders a list of templates in order.
        ///
        /// Returns a TemplateResult rather than the sequence, because that is what it
        /// returns under Fable, where it is `unbox items`. Here it is a template with a
        /// single hole holding the sequence, which the renderer already knows how to
        /// walk.
        static member ofList(items: TemplateResult list) : TemplateResult =
            { Segments = [| ""; "" |]
              Values = [| box (items :> seq<TemplateResult>) |] }
