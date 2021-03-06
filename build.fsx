// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

#r @"packages/FAKE/tools/FakeLib.dll"
open Fake
open Fake.Git
open Fake.AssemblyInfoFile
open Fake.ReleaseNotesHelper
open System
open System.IO

// --------------------------------------------------------------------------------------
// START TODO: Provide project-specific details below
// --------------------------------------------------------------------------------------

// Information about the project are used
//  - for version and project name in generated AssemblyInfo file
//  - by the generated NuGet package
//  - to run tests and to publish documentation on GitHub gh-pages
//  - for documentation, you also need to edit info in "docs/tools/generate.fsx"

// The name of the project
// (used by attributes in AssemblyInfo, name of a NuGet package and directory in 'src')
let project = "XPlot"

// Short summary of the project
// (used as description in AssemblyInfo and as a short summary for NuGet package)
let summary = "Data visualization library for F#"

// Longer description of the project
// (used as a description for NuGet package; line breaks are automatically cleaned up)
let description = "XPlot is a cross-platform data visualization library that supports creating charts using Google Charts and Plotly. The library provides a composable domain specific language for building charts and specifying their properties."

// List of author names (for NuGet package)
let authors = [ "Taha Hachana"; "Tomas Petricek" ]

// Tags for your project (for NuGet package)
let tags = "f# fsharp data visualization html5 javascript datavis google chart plotly deedle frame dataframe"

// File system information
let solutionFile  = "XPlot.sln"

// Pattern specifying assemblies to be tested using NUnit
let testAssemblies = "tests/**/bin/Release/*Tests*.dll"

// Git configuration (used for publishing documentation in gh-pages branch)
// The profile where the project is posted
let gitOwner = "fslaborg"
let gitHome = "https://github.com/" + gitOwner

// The name of the project on GitHub
let gitName = "XPlot"

// The url for the raw files hosted
let gitRaw = environVarOrDefault "gitRaw" "https://raw.github.com/fslaborg"

// --------------------------------------------------------------------------------------
// END TODO: The rest of the file includes standard build steps
// --------------------------------------------------------------------------------------

// Read additional information from the release notes document
System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__
let release = LoadReleaseNotes "RELEASE_NOTES.md"

// Generate assembly info files with the right version & up-to-date information
Target "AssemblyInfo" (fun _ ->
    let getAssemblyInfoAttributes projectName =
        [ Attribute.Title (projectName)
          Attribute.Product project
          Attribute.Description summary
          Attribute.Version release.AssemblyVersion
          Attribute.FileVersion release.AssemblyVersion ]

    let getProjectDetails projectPath =
        let projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath)
        ( projectPath, projectName,
          System.IO.Path.GetDirectoryName(projectPath),
          (getAssemblyInfoAttributes projectName) )

    !! "src/**/*.fsproj"
    |> Seq.map getProjectDetails
    |> Seq.iter (fun (projFileName, projectName, folderName, attributes) ->
        CreateFSharpAssemblyInfo (folderName @@ "AssemblyInfo.fs") attributes )
)

// --------------------------------------------------------------------------------------
// Clean build results

Target "Clean" (fun _ ->
    CleanDirs ["bin"; "temp"]
)

Target "CleanDocs" (fun _ ->
    CleanDirs ["docs/output"]
)

// --------------------------------------------------------------------------------------
// Build library & test project

Target "Build" (fun _ ->
    !! solutionFile
    |> MSBuildRelease "" "Rebuild"
    |> ignore
)

// --------------------------------------------------------------------------------------
// Run the unit tests using test runner

Target "RunTests" (fun _ ->
    !! testAssemblies
    |> NUnit (fun p ->
        { p with
            DisableShadowCopy = true
            TimeOut = TimeSpan.FromMinutes 20.
            OutputFile = "TestResults.xml" })
)

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target "NuGet" (fun _ ->
    Paket.Pack(fun p ->
        { p with
            OutputPath = "bin"
            Version = release.NugetVersion
            ReleaseNotes = toLines release.Notes
        })
)

Target "PublishNuget" (fun _ ->
    Paket.Push(fun p ->
        { p with
            WorkingDir = "bin"
            ApiKey = getBuildParamOrDefault "NugetKey" "" })
)

// --------------------------------------------------------------------------------------
// Generate the documentation

let generateHelp fail =
    let args = ["--define:RELEASE"; "--define:HELP"]
    if executeFSIWithArgs "docsrc/tools" "generate.fsx" args [] then
        traceImportant "Help generated"
    else
        if fail then failwith "generating help documentation failed"
        else traceImportant "generating help documentation failed"


Target "GenerateReferenceDocs" (fun _ ->
    if not (executeFSIWithArgs "docsrc/tools" "generate.fsx" ["--define:RELEASE"; "--define:REFERENCE"] []) then
      failwith "generating reference documentation failed"
)

Target "GenerateHelp" (fun _ ->
    DeleteFile "docsrc/content/release-notes.md"
    CopyFile "docsrc/content/" "RELEASE_NOTES.md"
    Rename "docsrc/content/release-notes.md" "docsrc/content/RELEASE_NOTES.md"

    DeleteFile "docsrc/content/license.md"
    CopyFile "docsrc/content/" "LICENSE.md"
    Rename "docsrc/content/license-lowercase.md" "docsrc/content/LICENSE.md"
    Rename "docsrc/content/license.md" "docsrc/content/license-lowercase.md"

    generateHelp true
)

Target "GenerateDocs" DoNothing

// --------------------------------------------------------------------------------------
// Release Scripts

#load "paket-files/fsharp/FAKE/modules/Octokit/Octokit.fsx"
open Octokit

Target "Release" (fun _ ->
    StageAll ""
    Git.Commit.Commit "" (sprintf "Bump version to %s" release.NugetVersion)
    Branches.push ""

    Branches.tag "" release.NugetVersion
    Branches.pushTag "" "origin" release.NugetVersion

    // release on github
    createClient (getBuildParamOrDefault "github-user" "") (getBuildParamOrDefault "github-pw" "")
    |> createDraft gitOwner gitName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes
    // TODO: |> uploadFile "PATH_TO_FILE"
    |> releaseDraft
    |> Async.RunSynchronously
)

Target "BuildPackage" DoNothing

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override

Target "All" DoNothing

"Clean"
  ==> "AssemblyInfo"
  ==> "Build"
  =?> ("GenerateReferenceDocs",isLocalBuild)
  =?> ("GenerateDocs",isLocalBuild)
  ==> "All"

"All"
  ==> "NuGet"

"CleanDocs"
  ==> "GenerateHelp"
  ==> "GenerateReferenceDocs"
  ==> "GenerateDocs"

"GenerateDocs"
  ==> "Release"

"BuildPackage"
  ==> "PublishNuget"
  ==> "Release"

RunTargetOrDefault "All"
