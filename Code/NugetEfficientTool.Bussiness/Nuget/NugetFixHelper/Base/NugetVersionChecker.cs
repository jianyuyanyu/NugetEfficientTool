﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NugetEfficientTool.Business
{
    /// <summary>
    /// Nuget问题检查器
    /// </summary>
    public class NugetVersionChecker
    {
        /// <summary>
        /// 构造一个 Nuget 版本检查器
        /// </summary>
        /// <param name="solutionFilePath">解决方案路径</param>
        public NugetVersionChecker(string solutionFilePath)
        {
            _solutionFilePath = solutionFilePath;
        }

        #region 对外字段 & 方法

        /// <summary>
        /// 检测Nuget问题
        /// </summary>
        public void Check()
        {
            var solutionFilePath = _solutionFilePath;
            if (string.IsNullOrEmpty(solutionFilePath))
            {
                throw new ArgumentNullException(nameof(solutionFilePath));
            }

            var projectFiles = GetProjectFiles(solutionFilePath);
            var projectDirectories = projectFiles.Select(Path.GetDirectoryName);
            var nugetConfigFiles = new List<string>();
            foreach (var projectDirectory in projectDirectories)
            {
                nugetConfigFiles.AddRange(SolutionFileHelper.GetNugetConfigFiles(projectDirectory));
            }
            //获取nuget相关信息
            var badFormatNugetConfigList = new List<NugetConfigReader>();
            var goodFormatNugetInfoExList = new List<FileNugetInfo>();
            foreach (var nugetConfigFile in nugetConfigFiles)
            {
                var nugetConfigReader = new NugetConfigReader(nugetConfigFile);
                if (nugetConfigReader.IsGoodFormat())
                {
                    goodFormatNugetInfoExList.AddRange(nugetConfigReader.PackageInfoExs);
                }
                else
                {
                    badFormatNugetConfigList.Add(nugetConfigReader);
                }
            }
            //格式问题及版本问题
            ErrorFormatNugetConfigs = badFormatNugetConfigList;
            MismatchVersionNugetInfoExs = GetMismatchVersionNugets(goodFormatNugetInfoExList);
            //设置nuget问题异常显示
            var nugetMismatchVersionMessage = CreateNugetMismatchVersionMessage(MismatchVersionNugetInfoExs);
            foreach (var errorFormatNugetConfig in ErrorFormatNugetConfigs)
            {
                Message = StringSplicer.SpliceWithDoubleNewLine(Message, errorFormatNugetConfig.ErrorMessage);
            }
            Message = StringSplicer.SpliceWithDoubleNewLine(Message, nugetMismatchVersionMessage);
            if (string.IsNullOrEmpty(Message))
            {
                Message = "完美无瑕！";
            }
        }

        private List<string> GetProjectFiles(string solutionFilePath)
        {
            List<string> solutionFiles = new List<string>();
            if (File.Exists(solutionFilePath))
            {
                solutionFiles.Add(solutionFilePath);
            }
            else if (Directory.Exists(solutionFilePath) &&
                     SolutionFileHelper.TryGetSlnFiles(solutionFilePath, out var slnFiles))
            {
                solutionFiles.AddRange(slnFiles);
            }
            //获取所有解决方案的项目列表
            var projectFiles = solutionFiles.SelectMany(SolutionFileHelper.GetProjectFiles).ToList();
            return projectFiles;
        }

        /// <summary>
        /// 异常 Nuget 配置文件列表
        /// </summary>
        public IEnumerable<NugetConfigReader> ErrorFormatNugetConfigs { get; private set; }

        public IEnumerable<FileNugetInfoGroup> MismatchVersionNugetInfoExs { get; private set; }

        /// <summary>
        /// 检测信息
        /// </summary>
        public string Message { get; private set; }

        #endregion

        #region 私有方法

        private IEnumerable<FileNugetInfoGroup> GetMismatchVersionNugets(
             IEnumerable<FileNugetInfo> nugetPackageInfoExs)
        {
            var mismatchVersionNugetGroupList = new List<FileNugetInfoGroup>();
            var nugetPackageInfoGroups = nugetPackageInfoExs.GroupBy(x => x.Name);
            foreach (var nugetPackageInfoGroup in nugetPackageInfoGroups)
            {
                //因为CsProj与package获取nuget信息，都有一定缺陷，所以需要彼此信息进行补偿。
                CompensateNugetInfos(nugetPackageInfoGroup.ToList());
                //筛选掉没问题的数据
                if (nugetPackageInfoGroup.Select(x => x.Version).Distinct().Count() == 1)
                {
                    continue;
                }

                mismatchVersionNugetGroupList.Add(new FileNugetInfoGroup(nugetPackageInfoGroup));
            }

            return mismatchVersionNugetGroupList;
        }
        /// <summary>
        /// 对Nuget信息进行补偿
        /// </summary>
        /// <param name="nugetInfoExs"></param>
        /// <returns></returns>
        private void CompensateNugetInfos(IEnumerable<FileNugetInfo> nugetInfoExs)
        {
            var nugetInfoExGroups = nugetInfoExs.GroupBy(i => Path.GetDirectoryName(i.ConfigPath)).ToList();
            foreach (var nugetInfoExGroup in nugetInfoExGroups)
            {
                var nugetInfoExsInGroup = nugetInfoExGroup.ToList();
                if (nugetInfoExsInGroup.Count != 2)
                {
                    continue;
                }

                var csProjNugetInfoEx = nugetInfoExsInGroup.FirstOrDefault(i => Path.GetExtension(i.ConfigPath) == ".csproj");
                var packageNugetInfoEx = nugetInfoExsInGroup.FirstOrDefault(i => Path.GetExtension(i.ConfigPath) == ".config");
                if (csProjNugetInfoEx == null || packageNugetInfoEx == null)
                {
                    continue;
                }
                csProjNugetInfoEx.TargetFramework = packageNugetInfoEx.TargetFramework;
                csProjNugetInfoEx.Version = packageNugetInfoEx.Version;
                packageNugetInfoEx.NugetDllInfo = csProjNugetInfoEx.NugetDllInfo;
            }
        }

        private string CreateNugetMismatchVersionMessage(
             IEnumerable<FileNugetInfoGroup> mismatchVersionNugetInfoExs)
        {
            var nugetMismatchVersionMessage = string.Empty;
            foreach (var mismatchVersionNugetInfoEx in mismatchVersionNugetInfoExs)
            {
                var headMessage = $"{mismatchVersionNugetInfoEx.NugetName} 存在版本异常：";
                var detailMessage = string.Empty;
                foreach (var nugetPackageInfo in mismatchVersionNugetInfoEx.FileNugetInfos)
                {
                    var mainDetailMessage = $"  {nugetPackageInfo.Version}，{nugetPackageInfo.ConfigPath}";
                    detailMessage = StringSplicer.SpliceWithNewLine(detailMessage, mainDetailMessage);
                }

                var singleNugetMismatchVersionMessage = StringSplicer.SpliceWithNewLine(headMessage, detailMessage);
                nugetMismatchVersionMessage = StringSplicer.SpliceWithDoubleNewLine(nugetMismatchVersionMessage,
                    singleNugetMismatchVersionMessage);
            }

            return nugetMismatchVersionMessage;
        }

        #endregion

        #region private fields

        private readonly string _solutionFilePath;

        #endregion
    }
}