#if BOOTSTRAP
System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
if not (System.IO.File.Exists "paket.exe") then let url = "https://github.com/fsprojects/Paket/releases/download/3.13.3/paket.exe" in use wc = new System.Net.WebClient() in let tmp = System.IO.Path.GetTempFileName() in wc.DownloadFile(url, tmp); System.IO.File.Move(tmp,System.IO.Path.GetFileName url);;
#r "paket.exe"
Paket.Dependencies.Install (System.IO.File.ReadAllText "paket.dependencies")
#endif

//---------------------------------------------------------------------

#I "packages/Suave/lib/net40"
#r "packages/Suave/lib/net40/Suave.dll"
#r "packages/FSharp.Data/lib/net40/FSharp.Data.dll"
#r "packages/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

open System
open System.IO
open FSharp.Data
open Suave
open Suave.Operators
open Suave.Http
open Suave.Filters
open Suave.Writers
open Suave.Successful
open Suave.RequestErrors
open Suave.Web
open Suave.CORS
open Suave.Json
open System.Net
open Newtonsoft.Json

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

let xmlMime = Writers.setMimeType "application/xml"
let jsonMime = Writers.setMimeType "application/json"

let corsConfig = 
    { defaultCORSConfig with 
        allowedUris = InclusiveOption.All
        exposeHeaders = true
        allowedMethods = InclusiveOption.Some [ HttpMethod.GET; HttpMethod.DELETE; HttpMethod.PATCH ] }

[<CLIMutable>]
type TodoItem = { title : string; completed : bool; url : string; order : int }
type TodoItemProvider = JsonProvider<""" { "title":"title", "completed":false, "order":1 } """>

let mutable todoItems = []

let handlePostRequest request =
  let postedItem = System.Text.Encoding.UTF8.GetString(request.rawForm) |> JsonConvert.DeserializeObject<TodoItem>
  let item = { postedItem with completed = false; url = (request.url.AbsoluteUri + Guid.NewGuid().ToString()) }
  todoItems <- item :: todoItems
  OK (JsonConvert.SerializeObject item)
let getTodoItems() =
  JsonConvert.SerializeObject todoItems

let getTodoItem url =
  match todoItems |> List.tryFind (fun item -> item.url = url) with
  | Some item -> OK (JsonConvert.SerializeObject item)
  | _ -> NOT_FOUND (sprintf "Item with URL %s not found." url)

let updateTodoItem request =
  let patchItem = System.Text.Encoding.UTF8.GetString(request.rawForm) |> JsonConvert.DeserializeObject<TodoItem>
  let patchTitle item =
    match item.url with
    | x when x = request.url.AbsoluteUri -> 
      { item with title = patchItem.title; completed = patchItem.completed; order = patchItem.order }
    | _ -> item
  todoItems <- (todoItems |> List.map patchTitle)
  getTodoItem request.url.AbsoluteUri

let app = 
  cors corsConfig >=>
  choose
    [ 
      OPTIONS 
        >=> NO_CONTENT
      GET
        >=> choose [ 
            path "/" >=> request (fun _ -> OK (getTodoItems()))
            pathScan "/%s" (fun _ -> request (fun r -> getTodoItem r.url.AbsoluteUri))
          ]
      POST 
        >=> request handlePostRequest
      DELETE 
        >=> request (fun _ -> todoItems <- []; OK "")
      PATCH
        >=> pathScan "/%s" (fun _ -> request (fun r -> updateTodoItem r))
    ]
    
#if DO_NOT_START_SERVER
#else
printfn "starting web server..."
startWebServer config app
printfn "exiting server..."
#endif

