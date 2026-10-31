/// Serving a server-rendered page its client code, under `npm run dev` and in production.
///
/// Through the real ASP.NET pipeline, in memory, with a stand-in for Vite listening on a
/// real port: the proxy is an HttpClient to a URL, so nothing less proves it reaches the
/// dev server. What each test asks is which of the three answered -- the app, Vite or
/// wwwroot -- because the failures this code exists to prevent all look like a page that
/// loaded fine.
module Lit.Server.Tests.Hosting

open System
open System.IO
open System.Net
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Hosting.Server.Features
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Options
open Xunit
open Lit

let private text (node: HtmlTypeProvider.Node) =
    let sb = StringBuilder()
    node.Invoke(sb)
    sb.ToString()

/// A stand-in for Vite on a free port, answering the entry module with a body that says
/// where it came from.
let private startDevServer () : Task<WebApplication> =
    task {
        let builder = WebApplication.CreateBuilder()
        builder.WebHost.UseUrls "http://127.0.0.1:0" |> ignore
        let dev = builder.Build()
        dev.MapGet("/App.js", Func<string>(fun () -> "from vite")) |> ignore
        do! dev.StartAsync()
        return dev
    }

let private addressOf (app: WebApplication) =
    app.Services.GetRequiredService<Server.IServer>().Features.Get<IServerAddressesFeature>().Addresses
    |> Seq.head

/// A wwwroot holding the bundle, so a test can tell the bundle from Vite's answer.
let private webRoot () =
    let dir = Directory.CreateTempSubdirectory "lit-hosting-"
    File.WriteAllText(Path.Combine(dir.FullName, "app.js"), "from wwwroot")
    dir.FullName

/// The app under test: its own page at /, and whatever LitClient adds.
let private startApp (client: LitClient) : Task<WebApplication> =
    task {
        let builder = WebApplication.CreateBuilder(client.Args)
        builder.WebHost.UseTestServer() |> ignore
        builder.AddLitClient client |> ignore
        let app = builder.Build()
        app.UseLitClient client |> ignore
        app.MapGet("/", Func<IResult>(fun () -> LitResults.page "<p>page</p>")) |> ignore
        do! app.StartAsync()
        return app
    }

let private get (app: WebApplication) (path: string) =
    task {
        let! response = app.GetTestClient().GetAsync(path)
        let! body = response.Content.ReadAsStringAsync()
        return response.StatusCode, body
    }

[<Fact>]
let ``--hmr turns hot reload on and is taken out of the arguments`` () =
    let client = LitClient.fromArgs "http://localhost:5173" [| "--urls"; "http://x"; "--hmr" |]
    Assert.True(client.HotReload)
    // ASP.NET does not know the flag; left in, it is a configuration key with no value.
    Assert.Equal<string[]>([| "--urls"; "http://x" |], client.Args)

[<Fact>]
let ``without --hmr it is production`` () =
    let client = LitClient.fromArgs "http://localhost:5173" [| "--urls"; "http://x" |]
    Assert.False(client.HotReload)
    Assert.Equal<string[]>([| "--urls"; "http://x" |], client.Args)

[<Fact>]
let ``production loads the bundle`` () =
    let client = LitClient.fromArgs "http://localhost:5173" [||]
    Assert.Equal("""<script type="module" src="/app.js"></script>""", text client.Scripts)

[<Fact>]
let ``hot reload loads vite's client and the entry module`` () =
    let client = LitClient.fromArgs "http://localhost:5173" [| "--hmr" |]

    Assert.Equal(
        """<script type="module" src="/@vite/client"></script><script type="module" src="/App.js"></script>""",
        text client.Scripts
    )

[<Fact>]
let ``a page starts with a doctype`` () =
    task {
        // HtmlTypeProvider renders the template's nodes, and a doctype is not one.
        // Without it the browser lays the page out in quirks mode.
        let client = LitClient.fromArgs "http://localhost:5173" [||]
        use! app = startApp client
        let! response = app.GetTestClient().GetAsync "/"
        let! body = response.Content.ReadAsStringAsync()
        Assert.Equal("<!doctype html>\n<p>page</p>", body)
        Assert.Equal("text/html", response.Content.Headers.ContentType.MediaType)
    }

[<Fact>]
let ``under hot reload the entry module comes from vite, even with a bundle in wwwroot`` () =
    task {
        // On a case-insensitive disk /App.js finds wwwroot/app.js. Answered from there,
        // the page runs the last build and ignores every edit, and nothing says so.
        use! dev = startDevServer ()
        let client = LitClient.fromArgs (addressOf dev) [| "--hmr"; "--webroot"; webRoot () |]
        use! app = startApp client

        let! status, body = get app "/App.js"
        Assert.Equal(HttpStatusCode.OK, status)
        Assert.Equal("from vite", body)
    }

[<Fact>]
let ``under hot reload the app's own routes are not proxied`` () =
    task {
        use! dev = startDevServer ()
        let client = LitClient.fromArgs (addressOf dev) [| "--hmr" |]
        use! app = startApp client

        let! _, body = get app "/"
        Assert.Equal("<!doctype html>\n<p>page</p>", body)
    }

[<Fact>]
let ``in production the bundle comes from wwwroot and nothing goes to vite`` () =
    task {
        use! dev = startDevServer ()
        let client = LitClient.fromArgs (addressOf dev) [| "--webroot"; webRoot () |]
        use! app = startApp client

        let! status, body = get app "/app.js"
        Assert.Equal(HttpStatusCode.OK, status)
        Assert.Equal("from wwwroot", body)

        // The entry module is a dev server's business; production has no answer for it.
        let! status, _ = get app "/@vite/client"
        Assert.Equal(HttpStatusCode.NotFound, status)
    }

[<Fact>]
let ``under hot reload the server lets go of its port at once`` () =
    task {
        // A restarter starts the next server while this one is still waiting for open
        // browser connections, and the next one cannot bind the port.
        let client = LitClient.fromArgs "http://localhost:5173" [| "--hmr" |]
        use! app = startApp client
        let options = app.Services.GetRequiredService<IOptions<HostOptions>>().Value
        Assert.Equal(TimeSpan.FromMilliseconds 200.0, options.ShutdownTimeout)
    }
