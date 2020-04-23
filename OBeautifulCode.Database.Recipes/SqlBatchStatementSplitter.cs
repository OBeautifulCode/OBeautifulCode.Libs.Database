﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="SqlBatchStatementSplitter.cs" company="OBeautifulCode">
//   Copyright (c) OBeautifulCode 2018. All rights reserved.
// </copyright>
// <auto-generated>
//   Sourced from NuGet package. Will be overwritten with package update except in OBeautifulCode.Database.Recipes source.
// </auto-generated>
// --------------------------------------------------------------------------------------------------------------------

namespace OBeautifulCode.Database.Recipes
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;

    /// <summary>
    /// Splits an SQL batch statement into individual statements.
    /// </summary>
    /// <remarks>
    /// Adapted from <a href="http://blog.stpworks.com/archive/2010/02/22/how-to-split-sql-file-by-go-statement.aspx"/>
    /// Go statement must be on its own line.  Does not work for semicolon separator.
    /// </remarks>
#if !OBeautifulCodeDatabaseRecipesProject
    [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
    [System.CodeDom.Compiler.GeneratedCode("OBeautifulCode.Database.Recipes", "See package version number")]
    internal
#else
    public
#endif
    static class SqlBatchStatementSplitter
    {
        /// <summary>
        /// Regex pattern to split up an SQL statement.
        /// </summary>
        private static readonly Regex SqlStatementSeparatorRegex = new Regex(@"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        /// <summary>
        /// Splits a batch SQL statement into individual statements.
        /// </summary>
        /// <param name="batchSql">The batch SQL to process.</param>
        /// <returns>
        /// The individual SQL statements.
        /// </returns>
        public static IReadOnlyList<string> SplitSqlAndRemoveEmptyStatements(
            string batchSql)
        {
            var result = SqlStatementSeparatorRegex.Split(batchSql + "\n").Where(statement => !string.IsNullOrWhiteSpace(statement)).ToList();

            return result;
        }
    }
}
