System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
if not (System.IO.File.Exists "paket.exe") then 
  let url = "http://github.com/fsprojects/Paket/releases/download/3.35.3/paket.exe" in 
    use wc = new System.Net.WebClient() in 
      let tmp = System.IO.Path.GetTempFileName() in 
        wc.DownloadFile(url, tmp); System.IO.File.Move(tmp,System.IO.Path.GetFileName url);;
#r "paket.exe"
Paket.Dependencies.Install (System.IO.File.ReadAllText "paket.dependencies")

// include Fake lib
#r @"packages/FSharp.Compiler.Service/lib/net45/FSharp.Compiler.Service.dll"
#r @"packages/Suave/lib/net40/Suave.dll"
#r @"packages/FAKE/tools/FakeLib.dll"
open Fake
open Suave
open System
open System.IO
open Microsoft.FSharp.Compiler.Interactive.Shell

let sbOut = Text.StringBuilder()
let sbErr = Text.StringBuilder()

let fsiSession =
  let inStream = new StringReader("")
  let outStream = new StringWriter(sbOut)
  let errStream = new StringWriter(sbErr)
  let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration()
  let argv = Array.append [|"/fake/fsi.exe"; "--quiet"; "--noninteractive"; "-d:DO_NOT_START_SERVER"|] [||]
  FsiEvaluationSession.Create(fsiConfig, argv, inStream, outStream, errStream)

let reportFsiError (e:exn) =
  traceError "Reloading app.fsx script failed."
  traceError (sprintf "Message: %s\nError: %s" e.Message (sbErr.ToString().Trim()))
  sbErr.Clear() |> ignore

let reloadScript () =
  try
    //Reload application
    traceImportant "Reloading app.fsx script..."
    let appFsx = __SOURCE_DIRECTORY__ @@ "app.fsx"
    fsiSession.EvalInteraction(sprintf "#load @\"%s\"" appFsx)
    fsiSession.EvalInteraction("open App")
    match fsiSession.EvalExpression("app") with
    | Some app -> Some(app.ReflectionValue :?> WebPart)
    | None -> failwith "Couldn't get 'app' value"
  with e -> reportFsiError e; None

let currentApp = ref (fun _ -> async { return None })

let rec findPort port =
  try
    let tcpListener = System.Net.Sockets.TcpListener(System.Net.IPAddress.Parse("127.0.0.1"), port)
    tcpListener.Start()
    tcpListener.Stop()
    port
  with :? System.Net.Sockets.SocketException as ex ->
    findPort (port + 1)

let getLocalServerConfig port =
  { defaultConfig with
      homeFolder = Some __SOURCE_DIRECTORY__
      bindings = [ HttpBinding.createSimple HTTP  "127.0.0.1" port ] }

let reloadAppServer (changedFiles: string seq) =
  traceImportant <| sprintf "Changes in %s" (String.Join(",",changedFiles))
  reloadScript() |> Option.iter (fun app ->
    currentApp.Value <- app
    traceImportant "Refreshed app." )

Target "run" (fun _ ->
  let app ctx = currentApp.Value ctx
  let port = findPort 8083
  let _, server = startWebServerAsync (getLocalServerConfig port) app

  // Start Suave to host it on localhost
  reloadAppServer ["app.fsx"]
  Async.Start(server)
  // Open web browser with the loaded file
  System.Diagnostics.Process.Start(sprintf "http://127.0.0.1:%d" port) |> ignore
  
  // Watch for changes & reload when app.fsx changes
  let sources = 
    { BaseDirectory = __SOURCE_DIRECTORY__
      Includes = [ "**/*.fsx"; "**/*.fs" ; "**/*.fsproj"; "web/content/app/*.js" ]; 
      Excludes = [] }
      
  use watcher = sources |> WatchChanges (Seq.map (fun x -> x.FullPath) >> reloadAppServer)  
  traceImportant "Waiting for app.fsx edits. Press any key to stop."
  Console.ReadLine() |> ignore
)

// start build
RunTargetOrDefault "run"

