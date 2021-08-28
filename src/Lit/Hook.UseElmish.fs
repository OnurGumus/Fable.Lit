// Adapted from https://github.com/Zaid-Ajaj/Feliz/blob/ba7b03cbc07e4ce0375809f925273427fad640f5/Feliz.UseElmish/UseElmish.fs
namespace Lit

open System
open Fable.Core
open Fable.Core.JsInterop

[<Struct>]
type internal RingState<'item> =
    | Writable of wx:'item array * ix:int
    | ReadWritable of rw:'item array * wix:int * rix:int

type internal RingBuffer<'item>(size) =
    let doubleSize ix (items: 'item array) =
        seq { yield! items |> Seq.skip ix
              yield! items |> Seq.take ix
              for _ in 0..items.Length do
                yield Unchecked.defaultof<'item> }
        |> Array.ofSeq

    let mutable state : 'item RingState =
        Writable (Array.zeroCreate (max size 10), 0)

    member _.Pop() =
        match state with
        | ReadWritable (items, wix, rix) ->
            let rix' = (rix + 1) % items.Length
            match rix' = wix with
            | true ->
                state <- Writable(items, wix)
            | _ ->
                state <- ReadWritable(items, wix, rix')
            Some items.[rix]
        | _ ->
            None

    member _.Push (item:'item) =
        match state with
        | Writable (items, ix) ->
            items.[ix] <- item
            let wix = (ix + 1) % items.Length
            state <- ReadWritable(items, wix, ix)
        | ReadWritable (items, wix, rix) ->
            items.[wix] <- item
            let wix' = (wix + 1) % items.Length
            match wix' = rix with
            | true ->
                state <- ReadWritable(items |> doubleSize rix, items.Length, 0)
            | _ ->
                state <- ReadWritable(items, wix', rix)

[<AutoOpen>]
module UseElmishExtensions =
    type Cmd<'msg> = (('msg -> unit) -> unit) list

    type Hook with
        static member useElmish<'State,'Msg>(this: HookDirective, init: 'State * Cmd<'Msg>, update: 'Msg -> 'State -> 'State * Cmd<'Msg>) =
            let init, cmd = init
            let state = this.useRef(fun () -> init)
            let ring = this.useRef(fun () -> RingBuffer(10))
            let childState, setChildState = this.useState(fun () -> init)
            let token = Hook.useCancellationToken(this)

            let setChildState () =
                JS.setTimeout(fun () ->
                    if not token.value.IsCancellationRequested then
                        setChildState state.value
                ) 0 |> ignore

            // TODO: Do we need useCallbackRef here?
            let rec dispatch (msg: 'Msg): unit =
                promise {
                    let mutable nextMsg = Some msg

                    while nextMsg.IsSome && not (token.value.IsCancellationRequested) do
                        let msg = nextMsg.Value
                        let (state', cmd') = update msg state.value
                        cmd' |> List.iter (fun sub -> sub dispatch)
                        nextMsg <- ring.value.Pop()
                        state.value <- state'
                        setChildState()
                }
                |> Promise.start

            this.useEffectOnce(fun () ->
                state.value <- init
                setChildState()

                cmd
                |> List.iter (fun sub -> sub dispatch)

                Hook.createDisposable(fun () ->
                    match box state.value with
                    | :? IDisposable as disp -> disp.Dispose()
                    | _ -> ())
                )

            this.useEffect(fun () -> ring.value.Pop() |> Option.iter dispatch)

            (childState, dispatch)

        static member inline useElmish<'State,'Msg> (init: 'State * Cmd<'Msg>, update: 'Msg -> 'State -> 'State * Cmd<'Msg>) =
            Hook.useElmish(jsThis, init, update)

        static member inline useElmish<'State,'Msg> (init: unit -> 'State * Cmd<'Msg>, update: 'Msg -> 'State -> 'State * Cmd<'Msg>) =
            let init = Hook.useMemo(init)
            Hook.useElmish(jsThis, init, update)