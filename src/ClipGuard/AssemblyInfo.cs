using System.Windows;

[assembly:ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]

// UpdateService.ChooseUpdate is the decision in the update check worth testing,
// and it has no business being public API. The test project reaches it directly
// rather than having the service widened for the sake of a test.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("ClipGuard.Tests")]
