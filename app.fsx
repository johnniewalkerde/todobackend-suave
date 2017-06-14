#if BOOTSTRAP
System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
if not (System.IO.File.Exists "paket.exe") then let url = "https://github.com/fsprojects/Paket/releases/download/3.13.3/paket.exe" in use wc = new System.Net.WebClient() in let tmp = System.IO.Path.GetTempFileName() in wc.DownloadFile(url, tmp); System.IO.File.Move(tmp,System.IO.Path.GetFileName url);;
#r "paket.exe"
Paket.Dependencies.Install (System.IO.File.ReadAllText "paket.dependencies")
#endif

//---------------------------------------------------------------------

#I "packages/Suave/lib/net40"
#r "packages/Suave/lib/net40/Suave.dll"

open System
open System.IO
open Suave                 // always open suave
open Suave.Operators
open Suave.Http
open Suave.Filters
open Suave.Writers
open Suave.Successful // for OK-result
open Suave.Web             // for config
open Suave.CORS
open Suave.Json
open System.Net

printfn "initializing script..."

let config = 
    let port = 
      let p = box (System.Environment.GetEnvironmentVariable("PORT")) 
      if isNull p then
        None
      else
        let port = unbox p
        Some(port |> string |> int)
    let ip127  = "127.0.0.1"
    let ipZero = "0.0.0.0"

    { defaultConfig with 
        logger = Logging.Targets.create Logging.Verbose [||]
        bindings=[ (match port with 
                     | None -> HttpBinding.createSimple HTTP ip127 8080
                     | Some p -> HttpBinding.createSimple HTTP ipZero p) ] }

printfn "starting web server..."

let jsonText n = 
    """
{"menu": {
  "id": "file",
  "value": "File",
  "popup": {
    "result": [
""" + String.concat "\n"
      [ for i in 1 .. n -> sprintf """{"value": "%d"},""" i ] + """
    ]
  }
}}""" 

let xmlMime = Writers.setMimeType "application/xml"
let jsonMime = Writers.setMimeType "application/json"
let plainTextMime = Writers.setMimeType "text/plain"

let setCORSHeaders =
    setHeader  "Access-Control-Allow-Origin" "*"
    >=> setHeader "Access-Control-Allow-Headers" "content-type"
    >=> setHeader "Access-Control-Allow-Methods" "POST, GET, OPTIONS, DELETE, PATCH"
let allowCors : WebPart =
    choose [
        OPTIONS >=> 
            fun context -> 
                context 
                |> (setCORSHeaders >=> OK "CORS approved")
    ]

let corsConfig = 
    { defaultCORSConfig with 
        allowedUris = InclusiveOption.All
        exposeHeaders = true
        allowedMethods = InclusiveOption.Some [ HttpMethod.GET ] }

let handlePostRequest request =
    System.Text.Encoding.UTF8.GetString(request.rawForm) |> OK
let app = 
  choose
    [ OPTIONS >=> cors corsConfig >=> NO_CONTENT
      GET 
      >=> path "/" 
      >=> cors corsConfig
      >=> OK ("Hello GET <br/>" 
        + corsConfig.allowedUris.ToString() 
        + "<br/>Expose Headers:" + corsConfig.exposeHeaders.ToString()
        + "<br/>Methods:" + corsConfig.allowedMethods.ToString()
        )
      POST >=> cors corsConfig >=> request handlePostRequest
    ]
    
#if DO_NOT_START_SERVER
#else
startWebServer config app
printfn "exiting server..."
#endif

