namespace Lit

open System
open System.Net.Http
open System.Runtime.CompilerServices
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.SpaServices
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open HtmlTypeProvider

/// Where a server-rendered page gets its client code.
///
/// Two answers, picked by the command line. In production the bundle is a file in
/// wwwroot. Under `npm run dev`, which passes `--hmr`, the same requests go to a dev
/// server (Vite) started alongside, which serves the modules unbundled and adds the
/// client that re-runs a changed module instead of reloading the page.
///
/// Started alongside rather than by this server, which is why it is not ASP.NET's
/// `UseReactDevelopmentServer`. A shared view is compiled into the server too, so editing
/// one restarts the server, and a dev server owned by the server would be restarted with
/// it: Fable from cold, and a browser told its dev server has gone.
type LitClient =
    { /// Whether the page is served from the dev server, which `--hmr` switches on.
      HotReload: bool
      /// The command line without `--hmr`, for WebApplication.CreateBuilder: ASP.NET
      /// would read the flag as a configuration key with no value.
      Args: string[]
      /// Where the dev server listens.
      DevServer: Uri
      /// The entry module, as the dev server serves it.
      Entry: string
      /// The bundle, as wwwroot serves it.
      Bundle: string }

    /// The page's script tags: the dev server's client and the entry module under hot
    /// reload, the bundle otherwise. Written raw, because they are this server's own.
    member this.Scripts: Node =
        if this.HotReload then
            Node.RawHtml
                $"""<script type="module" src="/@vite/client"></script><script type="module" src="{this.Entry}"></script>"""
        else
            Node.RawHtml $"""<script type="module" src="{this.Bundle}"></script>"""

module LitClient =

    /// Reads `--hmr` off the command line. The entry module is /App.js and the bundle
    /// /app.js, as Fable and Vite name them by default; a record update changes either.
    let fromArgs (devServer: string) (args: string[]) : LitClient =
        { HotReload = Array.contains "--hmr" args
          Args = args |> Array.filter (fun a -> a <> "--hmr")
          DevServer = Uri devServer
          Entry = "/App.js"
          Bundle = "/app.js" }

    /// Waits for the dev server, once, and then hands the proxy its address.
    ///
    /// The page renders from .NET whether or not anything can serve its code. Reached
    /// while the dev server is still starting, it looks finished and does nothing when a
    /// button is pressed. Waiting here keeps the markup immediate: only the request for
    /// the code waits.
    let internal readiness (client: LitClient) : Lazy<Task<Uri>> =
        lazy
            (task {
                use http = new HttpClient(Timeout = TimeSpan.FromSeconds 2.0)
                let deadline = DateTime.UtcNow.AddSeconds 90.0
                let mutable ready = false

                while not ready && DateTime.UtcNow < deadline do
                    try
                        let! response = http.GetAsync(Uri(client.DevServer, client.Entry))
                        ready <- response.IsSuccessStatusCode
                    with _ ->
                        ()

                    if not ready then
                        do! Task.Delay 500

                if not ready then
                    Console.Error.WriteLine
                        $"The dev server at {client.DevServer} never answered; the page will render but nothing will run."

                return client.DevServer
             })

/// Wiring a LitClient into an ASP.NET app: `AddLitClient` on the builder, `UseLitClient`
/// on the app, in that order and before the app's own endpoints are mapped.
[<Extension>]
type LitClientExtensions =

    /// Under hot reload, lets the server go at once when asked to stop.
    ///
    /// The default is to wait up to five seconds for open connections, and a browser
    /// keeps its connections open. A restarter has the next server running by then, which
    /// cannot bind the port, exits and is restarted, several times over.
    [<Extension>]
    static member AddLitClient(builder: WebApplicationBuilder, client: LitClient) : WebApplicationBuilder =
        if client.HotReload then
            builder.Services.Configure<HostOptions>(fun (options: HostOptions) ->
                options.ShutdownTimeout <- TimeSpan.FromMilliseconds 200.0)
            |> ignore

        builder

    /// Serves the client code: wwwroot in production, the dev server under hot reload.
    ///
    /// Under hot reload there are deliberately no static files. The page asks for
    /// /App.js and wwwroot holds app.js, which on a case-insensitive disk is the same
    /// request: the bundle answers, the dev server is never asked, and the page runs the
    /// last build's code, ignoring every edit.
    ///
    /// What goes to the dev server is whatever routing did not match. The proxy answers
    /// everything it is given, so the app's own routes are kept from it by asking for
    /// the endpoint rather than the path, and a route added later is covered without
    /// anyone coming back here.
    [<Extension>]
    static member UseLitClient(app: WebApplication, client: LitClient) : WebApplication =
        if client.HotReload then
            let ready = LitClient.readiness client

            app.UseWhen(
                (fun ctx -> isNull (ctx.GetEndpoint())),
                fun branch ->
                    branch.UseSpa(fun spa ->
                        spa.UseProxyToSpaDevelopmentServer(Func<Task<Uri>>(fun () -> ready.Value)))
            )
            |> ignore
        else
            app.UseStaticFiles() |> ignore

        app

module LitResults =

    /// A rendered page, as an HTML response.
    ///
    /// With the doctype put back. HtmlTypeProvider renders a template's nodes, and a
    /// doctype is not one, so the rendered page has none, and without it the browser lays
    /// the page out in quirks mode.
    let page (html: string) : IResult =
        Results.Content("<!doctype html>\n" + html, "text/html")
