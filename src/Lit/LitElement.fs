namespace Lit

open System
open Fable.Core
open Fable.Core.JsInterop
open Browser
open Browser.Types
open HMRTypes

// LitElement should inherit HTMLElement but HTMLElement
// is still implemented as interface in Fable.Browser
[<Import("LitElement", "lit")>]
type LitElement() =
    /// Node or ShadowRoot into which element DOM should be rendered. Defaults to an open shadowRoot.
    member _.renderRoot: HTMLElement = jsNative
    member _.shadowRoot: ShadowRoot = jsNative
    member _.isConnected: bool = jsNative
    member _.connectedCallback(): unit = jsNative
    member _.disconnectedCallback(): unit = jsNative
    member _.requestUpdate(): unit = jsNative
    /// Returns a promise that will resolve when the element has finished updating.
    member _.updateComplete: JS.Promise<unit> = jsNative
    // Internal: these are lit-element's own, declared so that a subclass here can reach
    // the originals, and not something to call from a component.
    member internal _.createRenderRoot(): obj = jsNative
    member internal _.update(changedProperties: obj): unit = jsNative

module private LitElementUtil =
    module Types =
        let [<Global>] String = obj()
        let [<Global>] Number = obj()
        let [<Global>] Boolean = obj()
        let [<Global>] Array = obj()
        let [<Global>] Object = obj()

    type Converter =
        abstract fromAttribute: JS.Function with get, set
        abstract toAttribute: JS.Function with get, set

    type PropConfig =
        abstract ``type``: obj with get, set
        abstract attribute: U2<string, bool> with get, set
        abstract state: bool with get, set
        abstract reflect: bool with get, set
        abstract noAccessor: bool with get, set
        abstract converter: Converter with get, set
        abstract hasChanged: JS.Function with get, set

    let isNotNull (x: obj) = not(isNull x)
    let isNotReferenceEquals (x: obj) (y: obj) = not(obj.ReferenceEquals(x, y))
    let failInit() = failwith "LitElement.init must be called on top of the render function"
    let failProps(key: string) = failwith $"'{key}' field in `props` record is not of Prop<'T> type"

    [<Emit("customElements.define($0, $1)")>]
    let defineCustomElement (name: string, cons: obj) = ()

    [<Emit("Object.defineProperty($0, $1, { get: $2 })")>]
    let defineGetter(target: obj, name: string, f: unit -> 'V) = ()

#if DEBUG
    let definedElements = Collections.Generic.HashSet<string>()

    let updateStyleSheets (data: obj) (litEl: LitElement) (newCSSResults: CSSResult[]) =
        if isNotNull litEl.shadowRoot && isNotNull litEl.shadowRoot.adoptedStyleSheets && isNotNull newCSSResults then
            let oldSheets = litEl.shadowRoot.adoptedStyleSheets
            let updatedSheets = getOrAdd data "updatedSheets" (fun _ -> JS.Constructors.Set.Create())
            if oldSheets.Length = newCSSResults.Length then
                Array.zip oldSheets newCSSResults
                |> Array.iter (fun (oldSheet, newCSSResult) ->
                    let newSheet = newCSSResult.styleSheet
                    if isNotNull newCSSResult.cssText && isNotReferenceEquals oldSheet newSheet && not(updatedSheets.has(newSheet)) then
                        oldSheet.replace(newCSSResult.cssText) |> Promise.start
                        updatedSheets.add(newSheet) |> ignore)
#endif

open LitElementUtil

type Prop internal (defaultValue: obj, options: obj) =
    member internal _.ToConfig() = defaultValue, options

    // Using static member instead of constructor in case we need to inline later
    // (e.g. to get the type of an empty array)

    /// <summary>
    /// Creates a property accessor.
    /// </summary>
    /// <param name="defaultValue">The initialization value.</param>
    /// <param name="attribute">Custom name of the HTML attribute (e.g. "my-prop"). Pass an empty string to disable exposing an HTML attribute. [More info](https://lit.dev/docs/components/properties/#observed-attributes).</param>
    /// <param name="hasChanged">Custom value change detection. [More info](https://lit.dev/docs/components/properties/#haschanged)</param>
    /// <param name="fromAttribute">Convert from the string attribute to the typed property. [More info](https://lit.dev/docs/components/properties/#conversion-converter).</param>
    /// <param name="toAttribute">Convert from the typed property to the string attribute. [More info](https://lit.dev/docs/components/properties/#conversion-converter).</param>
    /// <param name="reflect">When the property changes, reflect its value back to the HTML attribute (default: false). [More info](https://lit.dev/docs/components/properties/#reflected-attributes).</param>
    /// <returns>The property accessor.</returns>
    static member Of
        (
            defaultValue: 'T,
            ?attribute: string,
            ?hasChanged: 'T -> 'T -> bool,
            ?fromAttribute: string -> 'T,
            ?toAttribute: 'T -> string,
            ?reflect: bool
        ) =
        let options = jsOptions<PropConfig>(fun o ->
            let typ =
                match box defaultValue with
                | :? string -> Some Types.String
                | :? int | :? float -> Some Types.Number
                | :? bool -> Some Types.Boolean
                // TODO: Detect if it's an array of primitives or a record and use Array/Object
                | _ -> None
            typ |> Option.iter (fun v -> o.``type`` <- v)
            reflect |> Option.iter (fun v -> o.reflect <- v)
            hasChanged |> Option.iter (fun v -> o.hasChanged <- unbox v)
            attribute |> Option.iter (fun att ->
                match att.Trim() with
                // Let's use empty string to sign no attribute,
                // although we may need to be more explicit later
                | null | "" -> o.attribute <- !^false
                | att -> o.attribute <- !^att)
            match fromAttribute, toAttribute with
            | Some _, _ | _, Some _ ->
                o.converter <- jsOptions<Converter>(fun o ->
                    fromAttribute |> Option.iter (fun v -> o.fromAttribute <- unbox v)
                    toAttribute |> Option.iter (fun v -> o.toAttribute <- unbox v)
                )
            | _ -> ()
        )
        Prop<'T>(defaultValue, options)

and Prop<'T> internal (defaultValue: 'T, options: obj) =
    inherit Prop(defaultValue, options)
    [<Emit("$0{{ = $1}}")>]
    member _.Value with get() = defaultValue and set(_: 'T) = ()

/// Configuration values for the LitElement instances
type LitConfig<'Props> =
    ///<summary>
    /// An object containing the reactive properties definitions for the web components to react to changes
    /// </summary>
    /// <example>
    ///     {| color = Prop.Of("red", attribute = "my-color")
    ///        size = Prop.Of(100, attribute = "size") |}
    /// </example>
    abstract props: 'Props with get, set
    ///<summary>
    /// A list of CSS Styles that will be added to the LitElement's styles static property
    /// </summary>
    /// <example>
    ///     [ css $"""
    ///         :host { display: flex; }
    ///         .p { color: red; }
    ///       """]
    /// </example>
    abstract styles: CSSResult list with get, set
    /// Whether the element should render to shadow or light DOM (defaults to true).
    abstract useShadowDom: bool with get, set
    /// <summary>
    /// Whether the element takes part in forms (defaults to false).
    /// </summary>
    /// <remarks>
    /// Sets <c>static formAssociated</c> on the custom element, which is what lets a
    /// form see it at all: the element then gets <c>attachInternals()</c>, through which
    /// it can read the form it belongs to, contribute a value to submission, and be told
    /// when the form resets. Without it a custom control is a div that looks the part.
    ///
    /// The element still decides what it does with that. A submit button, for instance,
    /// is this plus a click handler calling <c>requestSubmit()</c> on the form its
    /// internals report.
    /// </remarks>
    /// <example>
    ///     LitElement.init(fun config ->
    ///         config.formAssociated &lt;- true)
    /// </example>
    abstract formAssociated: bool with get, set

type ILitElementInit<'Props> =
    abstract init: initFn: (LitConfig<'Props> -> JS.Promise<unit>) -> LitElement * 'Props

type LitElementInit<'Props>() =
    let mutable _initPromise: JS.Promise<unit> = null
    let mutable _useShadowDom = true
    let mutable _formAssociated = false
    let mutable _props = Unchecked.defaultof<'Props>
    let mutable _styles = Unchecked.defaultof<CSSResult list>

    member _.InitPromise = _initPromise

    interface LitConfig<'Props> with
        member _.props with get() = _props and set v = _props <- v
        member _.styles with get() = _styles and set v = _styles <- v
        member _.useShadowDom with get() = _useShadowDom and set v = _useShadowDom <- v
        member _.formAssociated with get() = _formAssociated and set v = _formAssociated <- v

    interface ILitElementInit<'Props> with
        member this.init initFn =
            _initPromise <- initFn this
            Unchecked.defaultof<_>

    interface IHookProvider with
        member _.hooks = failInit()

/// How a component comes to take over a shadow root the server drew.
///
/// A component can arrive with its shadow root already attached: the server wrote a
/// `<template shadowrootmode="open">` inside its tag (Lit.Server's `toShadowRootNode`),
/// and the HTML parser attached it while reading the page. Rendering into that root the
/// ordinary way draws a second copy beside the first, and the first -- the one the
/// reader has been looking at -- is wired to nothing.
///
/// Switched on from `Hydrate.elements` rather than here, so that this file goes on
/// importing nothing from @lit-labs/ssr-client. Hydrate is the one file that does.
module internal ElementAdoption =
    /// Filled by `Hydrate.elements`: adopts the markup in a root, or clears it so that an
    /// ordinary render can follow. Until it is filled a component renders as it always has.
    let mutable adopt: (TemplateResult * ShadowRoot * obj -> unit) option = None

    /// lit's own, and the half of lit-element's `createRenderRoot` that is still wanted
    /// when the root is already there.
    [<ImportMember("lit")>]
    let private adoptStyles (root: ShadowRoot) (styles: obj) : unit = jsNative

    /// The stylesheets lit-element worked out for this element's class when it was
    /// defined. Emitted, because the dynamic operator renames `constructor`.
    [<Emit("$0.constructor.elementStyles")>]
    let private stylesOf (element: obj) : obj = jsNative

    /// The component's own stylesheets, onto a root it did not create.
    let adoptOwnStyles (element: obj) (root: ShadowRoot) = adoptStyles root (stylesOf element)

    /// A root part marker directly inside the root, which is where every renderer that
    /// speaks lit's protocol writes one. A root carrying only a stylesheet -- sent ahead
    /// so the component is not unstyled while its script loads -- has none: there is no
    /// markup in it to adopt or to duplicate, and lit-element renders in front of the
    /// style element as it always has.
    [<Emit("Array.prototype.some.call($0.childNodes, (n) => n.nodeType === 8 && n.data.startsWith('lit-part'))")>]
    let holdsServerMarkup (root: ShadowRoot) : bool = jsNative

    /// Whether the page applied lit's own `lit-element-hydrate-support`, which gives
    /// LitElement an `observedAttributes` of its own where it only inherited one. A page
    /// that did has made its arrangements, and is left to them.
    [<Emit("Object.prototype.hasOwnProperty.call($0, 'observedAttributes')")>]
    let private litAdoptsForItself (litElement: obj) : bool = jsNative

    /// The duplicate described above used to arrive without a word. This is the word.
    let warnAboutToDuplicate (root: ShadowRoot) =
        if not (litAdoptsForItself jsConstructor<LitElement>) then
            let tag: string = root?host?localName

            console.warn (
                $"<{tag}> arrived with a shadow root the server rendered, and nothing is set to adopt it, so it is about to be rendered a second time beside the server's copy. Call Hydrate.elements() once at startup."
            )

[<AbstractClass; AttachMembers>]
type LitHookElement<'Props>(initProps: obj -> unit) =
    inherit LitElement()
    let _hooks = HookContext(jsThis)
    // Set between finding a root the server drew and the first update, which adopts it.
    let mutable _adopting = false
    // What that update rendered, held for the one call below that asks for it again.
    let mutable _adopted: TemplateResult option = None
#if DEBUG
    let mutable _hmrSub: IDisposable option = None
#endif
    do initProps(jsThis)

    abstract renderFn: JS.Function with get, set
    abstract __name: string

    member _.render() =
        match _adopted with
        | Some template ->
            _adopted <- None
            template
        | None -> _hooks.render()

    /// Takes the root that is already there instead of asking lit-element for one.
    ///
    /// Not through lit-element, for one reason: it remembers the root's first child as
    /// the node to render in front of, and lit then looks for its root part on that node
    /// rather than on the root. It would not find the part adoption is about to leave on
    /// the root, and would render a second copy after all. Returned as it stands, the
    /// root is where both look.
    ///
    /// The component's own stylesheets are adopted as they always are, alongside the
    /// `<style>` the server wrote. The server's covers the time before any script has
    /// run; the component's is the one a hot update replaces, and the only one there is
    /// when the server sent the markup without styles. The same rules twice is harmless.
    member this.createRenderRoot() : obj =
        let root = this.shadowRoot

        if isNull (box root) || not (ElementAdoption.holdsServerMarkup root) then
            base.createRenderRoot()
        else
            match ElementAdoption.adopt with
            | Some _ ->
                ElementAdoption.adoptOwnStyles this root
                _adopting <- true
                box root
            | None ->
                ElementAdoption.warnAboutToDuplicate root
                base.createRenderRoot()

    /// Adopts on the first update, then lets lit-element carry on as usual.
    ///
    /// Before lit-element's own update, not instead of it. What follows is an ordinary
    /// render of the template that was just adopted: lit finds the part adoption left on
    /// the root, sees every value is the one already there, and touches nothing. What it
    /// does do is hand lit-element that part, and lit-element only tells a tree that its
    /// element has left the page through the part it was handed. Skip this and a hook
    /// component inside an adopted element is never torn down -- which is exactly what
    /// lit's own `lit-element-hydrate-support` does, and why this is not built on it.
    ///
    /// The component's function runs once. `render` above hands lit-element the template
    /// made here rather than calling it a second time, which would run every
    /// `Hook.useEffect` of the first render twice.
    member this.update(changedProperties: obj) =
        if _adopting then
            match ElementAdoption.adopt with
            | Some adopt ->
                let template = _hooks.render ()
                // After the function has returned, not before: one that throws on its
                // first run and succeeds on a later one should still find the server's
                // markup waiting to be adopted, not render beside it.
                _adopting <- false
                _adopted <- Some template

                // As lit-element does ahead of its own first render, so that a tree
                // adopted while its element is out of the document knows as much.
                let options: obj = this?renderOptions
                options?isConnected <- this.isConnected

                adopt (template, this.shadowRoot, options)
            | None -> _adopting <- false

        base.update(changedProperties)

    member _.disconnectedCallback() =
        base.disconnectedCallback()
        _hooks.disconnect()

    /// It is possible, and it is ordinary: an element that is *moved* -- appended
    /// somewhere else, reordered by a drag, re-parented by a list re-render -- is
    /// disconnected and connected again, with the same instance and its state intact.
    /// `disconnectedCallback` has by then disposed everything `useEffectOnce` set up, so
    /// without this the second life of the element has no subscriptions, no listeners and
    /// no timers, and nothing anywhere reports that. The directive form of a hook
    /// component has always done this on reconnection; a LitElement did not.
    ///
    /// Safe on the first connection, which happens before the first render: effects are
    /// registered while rendering, so there are none to run yet, and the first run is
    /// still `HookContext.render`'s. The two cannot both fire for one connection.
    member _.connectedCallback() =
        base.connectedCallback()
        _hooks.reconnect()

#if DEBUG
    interface HMRSubscriber with
        member this.subscribeHmr = Some <| fun token ->
            match _hmrSub with
            | Some _ -> ()
            | None ->
                _hmrSub <-
                    token.Subscribe(fun info ->
                        _hooks.remove_css()
                        let updatedModule = info.NewModule
                        let updatedExport = updatedModule?(this.__name)
                        this.renderFn <- updatedExport?renderFn
                        updateStyleSheets info.Data this (updatedExport?styles)
                    )
                    |> Some
#endif

    interface ILitElementInit<'Props> with
        member this.init(_) = this :> LitElement, box this :?> 'Props

    interface IHookProvider with
        member _.hooks = _hooks

type LitElementAttribute(name: string) =
#if !DEBUG
    inherit JS.DecoratorAttribute()
    override this.Decorate(renderFn) =
#else
    inherit JS.ReflectedDecoratorAttribute()
    override _.Decorate(renderFn, mi) =
#endif
        let config = LitElementInit()
        let dummyFn() = failwith $"{name} is not immediately callable, it must be created in HTML"
        if renderFn.length > 0 then
            failwith "Render function for LitElement cannot take arguments"
        try
            renderFn.apply(config, [||]) |> ignore
        with _ -> ()

        if isNull config.InitPromise then
            failInit()

        config.InitPromise
        |> Promise.map (fun _ ->
            let config = config :> LitConfig<obj>

            let styles =
                if isNotNull config.styles then List.toArray config.styles |> Some
                else None

            let propsOptions, initProps =
                if isNotNull config.props then
                    let propsValues = ResizeArray()
                    let propsOptions = obj()

                    (JS.Constructors.Object.keys(config.props),
                     JS.Constructors.Object.values(config.props))
                    ||> Seq.zip
                    |> Seq.iter (fun (k, v) ->
                        let defVal, options =
                            match box v with
                            | :? Prop as v -> v.ToConfig()
                            // We could return `v, obj()` here but let's make devs used to
                            // initialize Props, which should make the code more consistent
                            | _ -> failProps(k)
                        propsOptions?(k) <- options
                        if not(isNull defVal) then
                            propsValues.Add(k, defVal))

                    let initProps (this: obj) =
                        propsValues |> Seq.iter(fun (k, v) ->
                            this?(k) <- v)

                    Some propsOptions, initProps
                else
                    None, fun _ -> ()

            let classExpr =
                let baseClass = jsConstructor<LitHookElement<obj>>
#if !DEBUG
                emitJsExpr (baseClass, renderFn, initProps) HookUtil.RENDER_FN_CLASS_EXPR
#else
                let renderRef = LitBindings.createRef()
                renderRef.value <- renderFn
                emitJsExpr (baseClass, renderRef, mi.Name, initProps) HookUtil.HMR_CLASS_EXPR
#endif

            propsOptions |> Option.iter (fun props -> defineGetter(classExpr, "properties", fun () -> props))
            styles |> Option.iter (fun styles -> defineGetter(classExpr, "styles", fun () -> styles))

            // A static getter rather than a second class expression, because that is all
            // `static formAssociated = true` is, and the browser reads it when the
            // element is defined.
            if config.formAssociated then
                defineGetter(classExpr, "formAssociated", fun () -> true)

            if not config.useShadowDom then
                emitJsStatement classExpr """$0.prototype.createRenderRoot = function() {
                    return this;
                }"""

#if !DEBUG
            defineCustomElement(name, classExpr)
#else
            // Build a key to avoid registering the element twice when hot reloading
            // We use the element name, the function name and the property names to minimize the chances of a false negative
            // (if there are two actual duplicated custom elements there should be an error indeed).
            let cacheName =
                match propsOptions with
                | None -> mi.Name + "::" + name
                | Some props -> mi.Name + "::" + name + "::" + (JS.Constructors.Object.keys(props) |> String.concat ", ")

            if not(definedElements.Contains(cacheName)) then
                defineCustomElement(name, classExpr)
                definedElements.Add(cacheName) |> ignore

            // This lets us access the updated render function when accepting new modules in HMR
            dummyFn?renderFn <- renderFn
            styles |> Option.iter (fun styles -> dummyFn?styles <- styles)
#endif
        )
        |> Promise.catchEnd (fun er ->
            console.error(er)
        )

        box dummyFn :?> _

[<AutoOpen>]
module LitElementExtensions =
    // TODO: Fix event constructors in Fable.Browser.Event
    [<Emit("new Event($0, $1)")>]
    let private createEvent (name: string) (opts: EventInit): Event = jsNative

    [<Emit("new CustomEvent($0, $1)")>]
    let private createCustomEvent (name: string) (opts: CustomEventInit<'T>): CustomEvent<'T> = jsNative

    type LitElement with
        /// <summary>
        /// Creates and Dispatches a Browser `Event`
        /// </summary>
        /// <param name="name">Name of the event to dispatch</param>
        /// <param name="bubbles">Allow the event to go through the bubling phase (default true)</param>
        /// <param name="composed">Allow the event to go through the shadow DOM boundary (default true)</param>
        /// <param name="cancelable">Allow the event to be cancelled (e.g. `event.preventDefault()`) (default true)</param>
        member this.dispatchEvent(name: string, ?bubbles: bool, ?composed: bool, ?cancelable: bool): unit =
            jsOptions<EventInit>(fun o ->
                o.bubbles <- defaultArg bubbles true
                o.composed <- defaultArg composed true
                o.cancelable <- defaultArg cancelable true
            )
            |> createEvent name
            |> this.renderRoot.dispatchEvent
            |> ignore

        /// <summary>
        /// Creates and Dispatches a Browser `CustomEvent`
        /// </summary>
        /// <param name="name">Name of the event to dispatch</param>
        /// <param name="detail">An optional value that will be available to any event listener via the `event.detail` property</param>
        /// <param name="bubbles">Allow the event to go through the bubling phase (default true)</param>
        /// <param name="composed">Allow the event to go through the shadow DOM boundary (default true)</param>
        /// <param name="cancelable">Allow the event to be cancelled (e.g. `event.preventDefault()`) (default true)</param>
        member this.dispatchCustomEvent(name: string, ?detail: 'T, ?bubbles: bool, ?composed: bool, ?cancelable: bool): unit =
            jsOptions<CustomEventInit<'T>>(fun o ->
                // Be careful if `detail` is not option, Fable may wrap it with `Some()`
                // as it's a generic and o.detail expects an option
                o.detail <- detail
                o.bubbles <- defaultArg bubbles true
                o.composed <- defaultArg composed true
                o.cancelable <- defaultArg cancelable true
            )
            |> createCustomEvent name
            |> this.renderRoot.dispatchEvent
            |> ignore

        /// <summary>
        /// Initializes the LitElement instance and registers the element in the custom elements registry
        /// </summary>
        static member inline init(): LitElement =
            jsThis<ILitElementInit<unit>>.init(fun _ -> Promise.lift ()) |> fst

        /// <summary>
        /// Initializes the LitElement instance, reactive properties and registers the element in the custom elements registry
        /// </summary>
        static member inline init(initFn: LitConfig<'Props> -> unit): LitElement * 'Props =
            jsThis<ILitElementInit<'Props>>.init(initFn >> Promise.lift)

        /// <summary>
        /// Initializes the LitElement instance, reactive properties and registers the element in the custom elements registry
        /// </summary>
        static member inline initAsync(initFn: LitConfig<'Props> -> JS.Promise<unit>): LitElement * 'Props =
            jsThis<ILitElementInit<'Props>>.init(initFn)
