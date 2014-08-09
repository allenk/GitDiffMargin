﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EnvDTE;
using LibGit2Sharp;
using Microsoft.VisualStudio.Text;
using Process = System.Diagnostics.Process;

namespace GitDiffMargin.Git
{
    public class GitCommands : IGitCommands
    {
        private readonly DTE _dte;

        public GitCommands(IServiceProvider serviceProvider)
        {
            _dte = (DTE) serviceProvider.GetService(typeof (_DTE));
        }

        private const int ContextLines = 0;

        public IEnumerable<HunkRangeInfo> GetGitDiffFor(ITextDocument textDocument, ITextSnapshot snapshot)
        {
            var filename = textDocument.FilePath;
            var discoveredPath = Repository.Discover(Path.GetFullPath(filename));

            if (!Repository.IsValid(discoveredPath)) yield break;

            using (var repo = new Repository(discoveredPath))
            {
                var retrieveStatus = repo.Index.RetrieveStatus(filename);
                if (retrieveStatus == FileStatus.Untracked || retrieveStatus == FileStatus.Added) yield break;

                var content = GetCompleteContent(textDocument, snapshot);
                if (content == null) yield break;

                using (var currentContent = new MemoryStream(content))
                {
                    var directoryInfo = new DirectoryInfo(discoveredPath).Parent;
                    if (directoryInfo == null) yield break;

                    var relativeFilepath = filename.Replace(directoryInfo.FullName + "\\", string.Empty);

                    var newBlob = repo.ObjectDatabase.CreateBlob(currentContent, relativeFilepath);

                    var from = TreeDefinition.From(repo.Head.Tip.Tree);

                    if (!repo.ObjectDatabase.Contains(from[relativeFilepath].TargetId)) yield break;

                    var blob = repo.Lookup<Blob>(from[relativeFilepath].TargetId);

                    var treeChanges = repo.Diff.Compare(blob, newBlob, new CompareOptions { ContextLines = ContextLines, InterhunkLines = 0 });

                    var gitDiffParser = new GitDiffParser(treeChanges.Patch, ContextLines);
                    var hunkRangeInfos = gitDiffParser.Parse();

                    foreach (var hunkRangeInfo in hunkRangeInfos)
                    {
                        yield return hunkRangeInfo;
                    }
                }
            }
        }

        private static byte[] GetCompleteContent(ITextDocument textDocument, ITextSnapshot snapshot)
        {
            var currentText = snapshot.GetText();

            var content = textDocument.Encoding.GetBytes(currentText);

            var preamble = textDocument.Encoding.GetPreamble();
            if (preamble.Length == 0) return content;

            var completeContent = new byte[preamble.Length + content.Length];
            Buffer.BlockCopy(preamble, 0, completeContent, 0, preamble.Length);
            Buffer.BlockCopy(content, 0, completeContent, preamble.Length, content.Length);

            return completeContent;
        }

        public void StartExternalDiff(ITextDocument textDocument)
        {
            if (textDocument == null || string.IsNullOrEmpty(textDocument.FilePath)) return;

            var filename = textDocument.FilePath;

            if (textDocument.IsDirty)
            {
                var docu = _dte.Documents.AllDocuments().FirstOrDefault(doc => doc.FullName == filename);
                if (docu != null)
                {
                    docu.Save();
                }
            };

            var discoveredPath = Repository.Discover(Path.GetFullPath(filename));

            using (var repo = new Repository(discoveredPath))
            {
                var diffGuiTool = repo.Config.Get<string>("diff.guitool");

                if (diffGuiTool == null) return;

                var diffCmd = repo.Config.Get<string>("difftool." + diffGuiTool.Value + ".path");

                var indexEntry = repo.Index[filename.Replace(repo.Info.WorkingDirectory, "")];
                var blob = repo.Lookup<Blob>(indexEntry.Id);

                var tempFileName = Path.GetTempFileName();
                File.WriteAllText(tempFileName, blob.GetContentText());
                    
                var process = new Process
                {
                    StartInfo =
                    {
                        FileName = diffCmd.Value,
                        Arguments = String.Format("{0} {1}", tempFileName, filename)
                    }
                };
                process.Start();
            }
        }

        public bool IsGitRepository(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) return false;
            var discoveredPath = Repository.Discover(Path.GetFullPath(directory));
            if (string.IsNullOrWhiteSpace(discoveredPath)) return false;
            var fullPath = Path.GetFullPath(discoveredPath);
            if (string.IsNullOrWhiteSpace(fullPath)) return false;
            return Repository.IsValid(fullPath);
        }

        public string GetGitRepository(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;
            var discoveredPath = Repository.Discover(Path.GetFullPath(filePath));
            if (string.IsNullOrWhiteSpace(discoveredPath)) return null;
            var fullPath = Path.GetFullPath(discoveredPath);
            var directoryInfo = Directory.GetParent(fullPath).Parent;
            return directoryInfo != null ? directoryInfo.FullName : null;
        }
    }
}