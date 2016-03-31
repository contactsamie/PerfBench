module Program

open PerfBench.Engine
open HttpClient
open FSharp.Data
open FSharp.Data.JsonExtensions
open System.Threading.Tasks

[<EntryPoint>]
let main argv = 
    let rnd = System.Random()

    let db = 
        if argv.Length > 0 then argv.[0]
        else ""
    
    let totalAgents = 
        if argv.Length > 1 then (int argv.[1])
        else 0
    
    let getRandomNumberAsString max =         
        string (rnd.Next(max))
    
    let getRandomNumber max = 
        rnd.Next(max)
    
    let GetUserByPost post = 
        async { 
            let baseUrl = "https://httpbin.org/get"
            printfn "response"
            let! response = createRequest Get baseUrl |> getResponseAsync
            printfn "got response"
            match response.StatusCode with
            | 200 -> 
                let parsedResponse = JsonValue.Parse(response.EntityBody.Value)
                let userId = parsedResponse?origin.AsString()
                printfn "%s\r\n" userId
            | _ -> printfn "Failed with code: %s" (string response.StatusCode)
            ()
        }
    
    let doStuff() = 
        async { 
            do! getRandomNumber 20000 |> Async.Sleep
            ()
        }
    
    let doStuffThatFails() = 
        async { 
            do! getRandomNumber 20000
                |> Task.Delay
                |> Async.AwaitTask
            if (getRandomNumber (2) > 0) then failwith ("fail")
            ()
        }
    
    do newSwarm "test" 100 doStuff
    do System.Diagnostics.Process.Start("http://localhost:8083") |> ignore
    System.Console.ReadLine() |> ignore
    0
