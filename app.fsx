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
open Microsoft.FSharp.Reflection
open Newtonsoft.Json
open Newtonsoft.Json.Converters

printfn "initializing script..."

type OptionConverter() =
    inherit JsonConverter()

    override x.CanConvert(t) = 
        t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

    override x.WriteJson(writer, value, serializer) =
        let value = 
            if isNull value then null
            else 
                let _,fields = FSharpValue.GetUnionFields(value, value.GetType())
                fields.[0]  
        serializer.Serialize(writer, value)

    override x.ReadJson(reader, t, existingValue, serializer) =        
        let innerType = t.GetGenericArguments().[0]
        let innerType = 
            if innerType.IsValueType then (typedefof<Nullable<_>>).MakeGenericType([|innerType|])
            else innerType        
        let value = serializer.Deserialize(reader, innerType)
        let cases = FSharpType.GetUnionCases(t)
        if isNull value then FSharpValue.MakeUnion(cases.[0], [||])
        else FSharpValue.MakeUnion(cases.[1], [|value|])

let getPort =
  let p = box (System.Environment.GetEnvironmentVariable("PORT")) 
  if isNull p then
    None
  else
    let port = unbox p
    Some(port |> string |> int)

let config = 
    let port = getPort
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
type TodoItem = { title : string option; completed : bool option; url : string option; order : int option }
type TodoItemProvider = JsonProvider<""" { "title":"title", "completed":false, "order":1 } """>

let mutable todoItems = []

let buildUrl (request:HttpRequest) =
  let port =
    match getPort with
    | Some p -> 80
    | None -> 8080
  request.url.Scheme + "://" +
  request.host + ":" + port.ToString() + "/" +
  Guid.NewGuid().ToString()

let handlePostRequest request =
  let postedItem = JsonConvert.DeserializeObject<TodoItem>(System.Text.Encoding.UTF8.GetString(request.rawForm), OptionConverter())
  let item = 
    { postedItem with 
        completed = Some false; 
        url = Some (buildUrl request)
    }
  todoItems <- item :: todoItems
  OK (JsonConvert.SerializeObject(item, OptionConverter()))
let getTodoItems() =
  JsonConvert.SerializeObject(todoItems, OptionConverter())

let getTodoItem guid =
  match todoItems |> List.tryFind (fun item -> item.url.Value.EndsWith(guid)) with
  | Some item -> OK (JsonConvert.SerializeObject(item, OptionConverter()))
  | _ -> NOT_FOUND (sprintf "Item with guid %s not found." guid)

let patchItem item withItem =
  { 
    item with 
      title = 
        match withItem.title with
        | Some t -> Some t
        | None -> item.title
      completed = 
        match withItem.completed with
        | Some c -> Some c
        | None -> item.completed
      order = 
        match withItem.order with
        | Some o -> Some o
        | None -> item.order
  }

let updateTodoItem request guid =
  let receivedItem = JsonConvert.DeserializeObject<TodoItem>(System.Text.Encoding.UTF8.GetString(request.rawForm), OptionConverter())
  let patchTitle item =
    match item.url with
    | x when x.Value.EndsWith(guid) -> patchItem item receivedItem
    | _ -> item
  todoItems <- (todoItems |> List.map patchTitle)
  getTodoItem guid

let app = 
  cors corsConfig >=>
  choose
    [ 
      OPTIONS 
        >=> NO_CONTENT
      GET
        >=> choose [ 
            path "/" >=> request (fun _ -> OK (getTodoItems()))
            pathScan "/%s" (fun guid -> request (fun r -> getTodoItem guid))
          ]
      POST 
        >=> request handlePostRequest
      DELETE 
        >=> request (fun _ -> todoItems <- []; OK "")
      PATCH
        >=> pathScan "/%s" (fun guid -> request (fun r -> updateTodoItem r guid))
    ]
    
#if DO_NOT_START_SERVER
#else
printfn "starting web server..."
startWebServer config app
printfn "exiting server..."
#endif

