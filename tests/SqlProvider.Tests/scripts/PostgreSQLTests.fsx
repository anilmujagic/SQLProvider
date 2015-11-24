#I @"../../../bin"
#r @"../../../bin/FSharp.Data.SqlProvider.dll"

open System
open FSharp.Data.Sql

[<Literal>]
let connStr = "User ID=colinbull;Host=localhost;Port=5432;Database=sqlprovider;"

[<Literal>]
let resolutionFolder = @"../../../packages/scripts/Npgsql/lib/net45"
FSharp.Data.Sql.Common.QueryEvents.SqlQueryEvent |> Event.add (printfn "Executing SQL: %s")

let processId = System.Diagnostics.Process.GetCurrentProcess().Id;

type HR = SqlDataProvider<Common.DatabaseProviderTypes.POSTGRESQL, connStr, ResolutionPath = resolutionFolder>
let ctx = HR.GetDataContext()

type Employee = {
    EmployeeId : int32
    FirstName : string
    LastName : string
    HireDate : DateTime
}

//***************** Individuals ***********************//
let indv = ctx.Public.Employees.Individuals.``As FirstName``.``100, Steven``


indv.FirstName + " " + indv.LastName + " " + indv.Email


//*************** QUERY ************************//
let employeesFirstName = 
    query {
        for emp in ctx.Public.Employees do
        select (emp.FirstName, emp.LastName)
    } |> Seq.toList

let salesNamedDavid = 
    query {
            for emp in ctx.Public.Employees do
            join d in ctx.Public.Departments on (emp.DepartmentId = d.DepartmentId)
            where (d.DepartmentName |=| [|"Sales";"IT"|] && emp.FirstName =% "David")
            select (d.DepartmentName, emp.FirstName, emp.LastName)
    } |> Seq.toList

let employeesJob = 
    query {
            for emp in ctx.Public.Employees do
            for manager in emp.``public.employees by employee_id`` do
            join dept in ctx.Public.Departments on (emp.DepartmentId = dept.DepartmentId)
            where ((dept.DepartmentName |=| [|"Sales";"Executive"|]) && emp.FirstName =% "David")
            select (emp.FirstName, emp.LastName, manager.FirstName, manager.LastName )
    } |> Seq.toList

//Can map SQLEntities to a domain type
let topSales5ByCommission = 
    query {
        for emp in ctx.Public.Employees do
        sortByDescending emp.CommissionPct
        select emp
        take 5
    } 
    |> Seq.map (fun e -> e.MapTo<Employee>())
    |> Seq.toList

#r @"../../../packages/scripts/Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"

open Newtonsoft.Json

type OtherCountryInformation = {
    Id : string
    Population : int
}

type Country = {
    CountryId : string
    CountryName : string
    Other : OtherCountryInformation
}

//Can customise SQLEntity mapping
let countries = 
    query {
        for emp in ctx.Public.Countries do
        select emp
    } 
    |> Seq.map (fun e -> e.MapTo<Country>(fun (prop,value) -> 
                                               match prop with
                                               | "Other" -> 
                                                    if value <> null
                                                    then JsonConvert.DeserializeObject<OtherCountryInformation>(value :?> string) |> box
                                                    else Unchecked.defaultof<OtherCountryInformation> |> box
                                               | _ -> value
                                         )
               )
    |> Seq.toList

//************************ CRUD *************************//


let antartica =
    let result =
        query {
            for reg in ctx.Public.Regions do
            where (reg.RegionId = 5)
            select reg
        } |> Seq.toList
    match result with
    | [ant] -> ant
    | _ -> 
        let newRegion = ctx.Public.Regions.Create() 
        newRegion.RegionName <- "Antartica"
        newRegion.RegionId <- 5
        ctx.SubmitUpdates()
        newRegion

antartica.RegionName <- "ant"
ctx.SubmitUpdates()

antartica.Delete()
ctx.SubmitUpdates()

//********************** Procedures **************************//

let removeIfExists employeeId startDate =
    let existing = query { for x in ctx.Public.JobHistory do
                           where ((x.EmployeeId = employeeId) && (x.StartDate = startDate))
                           headOrDefault }
    if existing <> null then
        existing.Delete()
        ctx.SubmitUpdates()

removeIfExists 100 (DateTime(1993, 1, 13))
ctx.Functions.AddJobHistory.Invoke(100, DateTime(1993, 1, 13), DateTime(1998, 7, 24), "IT_PROG", 60)

//Support for sprocs that return ref cursors
let employees =
    [
      for e in ctx.Functions.GetEmployees.Invoke().ReturnValue do
        yield e.MapTo<Employee>()
    ]

type Region = {
    RegionId : decimal
    RegionName : string
  //  RegionDescription : string
}

//Support for MARS procs
let locations_and_regions =
    let results = ctx.Functions.GetLocationsAndRegions.Invoke()
    [
//      for e in results.ReturnValue do
//        yield e.ColumnValues |> Seq.toList |> box
//             
      for e in results.ColumnValues do
        yield e// |> Seq.toList |> box
    ]


//Support for sprocs that return ref cursors and has in parameters
let getemployees hireDate =
    let results = (ctx.Functions.GetEmployeesStartingAfter.Invoke hireDate)
    [
      for e in results.ReturnValue do
        yield e.MapTo<Employee>()
    ]

getemployees (new System.DateTime(1999,4,1))

//********************** Functions ***************************//

let fullName = ctx.Functions.EmpFullname.Invoke(100).ReturnValue
