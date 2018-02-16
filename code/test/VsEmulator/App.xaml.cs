﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using Microsoft.Templates.Core;
using Microsoft.Templates.Core.Diagnostics;
using Microsoft.Templates.Core.Gen;
using Microsoft.Templates.Core.Locations;
using Microsoft.Templates.Core.PostActions.Catalog.Merge;
using Microsoft.Templates.Fakes;
using Microsoft.Templates.UI;
using Microsoft.Templates.UI.Services;
using Microsoft.Templates.UI.Threading;
using Microsoft.Templates.VsEmulator.Main;
using Microsoft.Templates.VsEmulator.Services;
using Microsoft.VisualStudio.TemplateWizard;

namespace Microsoft.Templates.VsEmulator
{
    public partial class App : Application
    {
        public App()
        {
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Any())
            {
                LaunchWizardFromCommandLineForAutomatedTesting(e.Args);
            }
        }

        private void LaunchWizardFromCommandLineForAutomatedTesting(string[] args)
        {
            SafeThreading.JoinableTaskFactory.Run(async () =>
            {
                var exitCode = 0;

                await SafeThreading.JoinableTaskFactory.SwitchToMainThreadAsync();
                try
                {
                    var options = new CommandLineOptions();

                    if (CommandLine.Parser.Default.ParseArguments(args, options))
                    {
                        var cultureArg = options.Culture;

                        if (!string.IsNullOrWhiteSpace(cultureArg))
                        {
                            var culture = new CultureInfo(cultureArg);

                            Thread.CurrentThread.CurrentCulture = culture;
                            Thread.CurrentThread.CurrentUICulture = culture;
                        }

                        var progLanguage = options.ProgLang;

                        var newProjectName = string.IsNullOrWhiteSpace(options.ProjectName)
                            ? Path.GetFileName(Path.GetTempFileName().Replace(".", string.Empty))
                            : options.ProjectName;

                        // TODO [ML]: add support for right-click wizards too.
                        GenContext.Bootstrap(
                            new LocalTemplatesSource("0.0.0.0", string.Empty),
                            new FakeGenShell(progLanguage),
                            new Version("0.0.0.0"),
                            progLanguage);

                        await GenContext.ToolBox.Repo.RefreshAsync();

                        GenContext.SetCurrentLanguage(progLanguage);
                        var fakeShell = GenContext.ToolBox.Shell as FakeGenShell;
                        fakeShell?.SetCurrentLanguage(progLanguage);

                        var newProjectLocation = Path.Combine(Path.GetTempPath(), "UiTest");

                        var projectPath = Path.Combine(newProjectLocation, newProjectName, newProjectName);

                        var context = new FakeContextProvider
                        {
                            ProjectName = newProjectName,
                            ProjectPath = projectPath,
                            OutputPath = Path.Combine(Path.GetTempPath(), newProjectName, newProjectName),
                            FailedMergePostActions = new List<FailedMergePostAction>(),
                            MergeFilesFromProject = new Dictionary<string, List<MergeInfo>>(),
                            FilesToOpen = new List<string>(),
                            ProjectItems = new List<string>(),
                            ProjectMetrics = new Dictionary<ProjectMetricsEnum, double>()
                        };

                        GenContext.Current = context;

                        // Need to create a MainViewModel as the styles in the UI of all pages is set up by it
                        var vm = new MainViewModel(null);
                        UIStylesService.Instance.Initialize(FakeStyleValuesProvider.Instance);

                        switch (options.UI.ToUpperInvariant())
                        {
                            case "PAGE":
                                Directory.CreateDirectory(projectPath);

                                // Add a manifest file with enough info for the wizard to function
                                var fakeAppxManifest = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Package
  xmlns=""http://schemas.microsoft.com/appx/manifest/foundation/windows10""
  xmlns:mp=""http://schemas.microsoft.com/appx/2014/phone/manifest""
  xmlns:uap=""http://schemas.microsoft.com/appx/manifest/uap/windows10""
  xmlns:genTemplate=""http://schemas.microsoft.com/appx/developer/windowsTemplateStudio""
  IgnorableNamespaces=""uap mp genTemplate"">
  <genTemplate:Metadata>
    <genTemplate:Item Name=""generator"" Value=""Windows Template Studio""/>
    <genTemplate:Item Name=""wizardVersion"" Version=""v0.0.0.0"" />
    <genTemplate:Item Name=""templatesVersion"" Version=""v0.0.0.0"" />
    <genTemplate:Item Name=""projectType"" Value=""Blank"" />
    <genTemplate:Item Name=""framework"" Value=""CodeBehind"" />
  </genTemplate:Metadata>
</Package>
";

                                File.WriteAllText(Path.Combine(projectPath, "package.appxmanifest"), fakeAppxManifest);

                                var userPageSelection = NewItemGenController.Instance.GetUserSelectionNewPage(GenContext.CurrentLanguage);

                                break;

                            case "FEATURE":
                                var userFeatureSelection = NewItemGenController.Instance.GetUserSelectionNewFeature(GenContext.CurrentLanguage);

                                break;

                            case "PROJECT":
                            default:
                                var userSelectionIsNotUsed = NewProjectGenController.Instance.GetUserSelection(progLanguage);

                                break;
                        }
                    }
                    else
                    {
                        try
                        {
                            // Ensure there's a console window available to display output
                            if (!NativeMethods.AttachConsole(-1))
                            {
                                NativeMethods.AllocConsole();
                            }

                            Console.WriteLine(options.GetUsage());
                            Console.ReadKey(true);
                        }
                        finally
                        {
                            NativeMethods.FreeConsole();
                            exitCode = -1;
                        }
                    }
                }
                catch (WizardBackoutException)
                {
                    // Get this if cancel out of the wizard
                }
                catch (Exception exception)
                {
                    MessageBox.Show(exception.Message, "Error");
                    exitCode = exception.HResult;
                }

                Application.Current.Shutdown(exitCode);
            });
        }
    }
}
