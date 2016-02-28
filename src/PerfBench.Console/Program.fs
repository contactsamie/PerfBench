open PerfBench.Engine;
open System
open HttpClient
open FSharp.Data
open FSharp.Data.JsonExtensions

[<EntryPoint>]
let main argv =
    let getRandomNumberAsString max =
        let rnd = System.Random()
        string (rnd.Next(max))

    let GetUserByPost post = async {        
        let baseUrl = "https://httpbin.org/get"
        printfn "response"
        let! response = createRequest Get baseUrl |> getResponseAsync
        printfn "got response"
        match response.StatusCode with
            | 200 -> 
                let parsedResponse = JsonValue.Parse(response.EntityBody.Value)
                let userId = parsedResponse?origin.AsString()
                printfn "%s\r\n" userId
            | _ -> 
                printfn "Failed with code: %s" (string response.StatusCode)
        ()
        }

    let l = runTest "user" 10 (fun() -> GetUserByPost "1")

    System.Console.ReadLine() |> ignore

    0
