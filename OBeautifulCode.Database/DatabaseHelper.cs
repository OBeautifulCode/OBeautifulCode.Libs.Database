﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="DatabaseHelper.cs" company="OBeautifulCode">
//   Copyright 2015 OBeautifulCode
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace OBeautifulCode.Database
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Data;
    using System.Data.SqlClient;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Security;

    using OBeautifulCode.Collection;
    using OBeautifulCode.String;

    using Spritely.Recipes;

    /// <summary>
    /// Provides various methods for interacting with a database.
    /// </summary>
    public static class DatabaseHelper
    {
        /// <summary>
        /// Opens an database connection using a Connection String.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="IDbConnection"/> to open.  Must be a class.</typeparam>
        /// <param name="connectionString">String used to open a connection to the database.</param>
        /// <returns>Returns an open connection of the specified <see cref="IDbConnection"/> type.</returns>
        /// <exception cref="ArgumentNullException">connectionString is null.</exception>
        /// <exception cref="ArgumentException">connectionString is whitespace.</exception>
        /// <exception cref="ArgumentException">connectionString isn't formatted properly.</exception>
        /// <exception cref="SqlException">A connection-level error occurred while opening the connection. If the Number property contains the value 18487 or 18488, this indicates that the specified password has expired or must be reset.</exception>
        public static T OpenConnection<T>(string connectionString) where T : class, IDbConnection, new()
        {
            new { connectionString }.Must().NotBeNull().And().NotBeWhiteSpace().OrThrowFirstFailure();

            T connection = null;
            try
            {
                // an invalid connectionString will throw ArgumentException.
                connection = new T { ConnectionString = connectionString };

                // InvalidOperationException won't be thrown, even if data source or server aren't specified
                // in the connection string.  as long as the connection string is valid,
                // the only possible exception is SqlException
                connection.Open();
            }
            catch (Exception)
            {
                if (connection != null)
                {
                    connection.Dispose();
                }

                throw;
            }

            return connection;
        }

        /// <summary>
        /// Builds an <see cref="IDbCommand"/> using an existing database connection.
        /// </summary>
        /// <param name="connection">An <see cref="IDbConnection"/> that represents the connection to a database.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.  Null parameters are ignored.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="transaction">The transaction within which the command will execute.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns the constructed <see cref="IDbCommand"/>.</returns>
        /// <exception cref="ArgumentNullException">connection is null.</exception>
        /// <exception cref="ArgumentException">connection is in an invalid state (must be Open).</exception>
        /// <exception cref="ArgumentNullException">commandText is null.</exception>
        /// <exception cref="ArgumentException">commandText is whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 0.</exception>
        /// <exception cref="ArgumentException">transaction is invalid (has been rolled back or committed).</exception>
        /// <exception cref="ArgumentException">transaction is using a different connection than the specified connection.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        public static IDbCommand BuildCommand(IDbConnection connection, string commandText, IEnumerable<IDataParameter> commandParameters = null, CommandType commandType = CommandType.Text, IDbTransaction transaction = null, bool prepareCommand = false, int timeoutSeconds = 0)
        {
            // check arguments
            new { connection }.Must().NotBeNull().OrThrow();
            new { connection.State }.Must().BeEqualTo(ConnectionState.Open).Because("connection is in an invalid state: " + connection.State + ".  Must be Open.").OrThrow();
            new { commandText }.Must().NotBeNull().And().NotBeWhiteSpace().OrThrowFirstFailure();
            new { timeoutSeconds }.Must().BeGreaterThanOrEqualTo(0).OrThrow();

            // validate transaction
            if (transaction != null)
            {
                if (transaction.Connection == null)
                {
                    throw new ArgumentException("transaction is invalid.");
                }

                if (transaction.Connection != connection)
                {
                    throw new ArgumentException("transaction is using a different connection than the specified connection.");
                }
            }

            // create the command
            IDbCommand command = connection.CreateCommand();

            try
            {
                // populate command properties
                command.Connection = connection;
                command.CommandType = commandType;
                command.CommandText = commandText;
                command.CommandTimeout = timeoutSeconds;
                if (transaction != null)
                {
                    command.Transaction = transaction;
                }

                // are there any parameters?  add them to command, replacing null with DBNull
                if (commandParameters != null)
                {
                    foreach (IDataParameter parameter in commandParameters)
                    {
                        if (parameter == null)
                        {
                            continue;
                        }

                        if (parameter.Value == null)
                        {
                            parameter.Value = DBNull.Value;
                        }

                        try
                        {
                            command.Parameters.Add(parameter);
                        }
                        catch (InvalidCastException)
                        {
                            throw new InvalidOperationException("Attempting to set a parameter of type " + parameter.GetType().Name + " that was designed for a data provider other than the provider represented by the specified connection.");
                        }
                    }
                }

                if (prepareCommand)
                {
                    command.Prepare();
                }

                return command;
            }
            catch (Exception)
            {
                command.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Executes the CommandText against the Connection and builds an IDataReader.
        /// </summary>
        /// <param name="connection">An <see cref="IDbConnection"/> that represents the connection to a database.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="transaction">The transaction within which the command will execute.</param>
        /// <param name="commandBehavior">Provides a description of the results of the query and its effect on the database.  This enumeration has a FlagsAttribute attribute that allows a bitwise combination of its member values.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns an <see cref="IDataReader"/>.</returns>
        /// From BuildCommand:
        /// <exception cref="ArgumentNullException">connection is null.</exception>
        /// <exception cref="ArgumentException">connection is in an invalid state (must be Open).</exception>
        /// <exception cref="ArgumentNullException">commandText is null.</exception>
        /// <exception cref="ArgumentException">commandText is whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 0.</exception>
        /// <exception cref="ArgumentException">transaction is invalid (has been rolled back or committed).</exception>
        /// <exception cref="ArgumentException">transaction is using a different connection than the specified connection.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// From this method:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// <exception cref="InvalidOperationException">Connection is pending a local transaction.</exception>
        /// <exception cref="InvalidOperationException">There is already an open SqlDataReader associated with the connection.</exception>
        /// <remarks>
        /// If an expected parameter type does not match an actual parameter value's type, ExecuteReader() does not throw <see cref="SqlException"/>.
        /// Instead, a reader with no rows is returned.  Any attempt to Read() will throw an exception.
        /// </remarks>
        public static IDataReader ExecuteReader(IDbConnection connection, string commandText, IDataParameter[] commandParameters = null, CommandType commandType = CommandType.Text, IDbTransaction transaction = null, CommandBehavior commandBehavior = CommandBehavior.Default, bool prepareCommand = false, int timeoutSeconds = 0)
        {
            using (IDbCommand command = BuildCommand(connection, commandText, commandParameters, commandType, transaction, prepareCommand, timeoutSeconds))
            {
                return command.ExecuteReader(commandBehavior);  // can throw SqlException
            }
        }

        /// <summary>
        /// Opens a connection to the database and executes the CommandText to build an IDataReader.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="IDbConnection"/> to open.  Must be a class.</typeparam>
        /// <param name="connectionString">String used to open a connection to the database.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="commandBehavior">Provides a description of the results of the query and its effect on the database.  This enumeration has a FlagsAttribute attribute that allows a bitwise combination of its member values.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns an <see cref="IDataReader"/>.</returns>
        /// From OpenConnection:
        /// <exception cref="ArgumentNullException">connectionString is null.</exception>
        /// <exception cref="ArgumentException">connectionString is whitespace.</exception>
        /// <exception cref="ArgumentException">connectionString isn't formatted properly.</exception>
        /// <exception cref="SqlException">A connection-level error occurred while opening the connection. If the Number property contains the value 18487 or 18488, this indicates that the specified password has expired or must be reset.</exception>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentNullException">commandText is null.</exception>
        /// <exception cref="ArgumentException">commandText is whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 0.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// From ExecuteReader:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// <remarks>
        /// If an expected parameter type does not match an actual parameter value's type, ExecuteReader() does not throw <see cref="SqlException"/>.
        /// Instead, a reader with no rows is returned.  Any attempt to Read() will throw an exception.
        /// </remarks>
        public static IDataReader ExecuteReader<T>(string connectionString, string commandText, IDataParameter[] commandParameters = null, CommandType commandType = CommandType.Text, CommandBehavior commandBehavior = CommandBehavior.Default, bool prepareCommand = false, int timeoutSeconds = 0) where T : class, IDbConnection, new()
        {
            var connection = OpenConnection<T>(connectionString);
            return ExecuteReader(connection, commandText, commandParameters, commandType, null, commandBehavior, prepareCommand, timeoutSeconds);
        }

        /// <summary>
        /// Executes a SQL statement against a connection object and returns the number of rows affected.
        /// </summary>
        /// <param name="connection">An <see cref="IDbConnection"/> that represents the connection to a database.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.  Null parameters are ignored.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="transaction">The transaction within which the command will execute.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns the number of rows affected.</returns>
        /// From BuildCommand:
        /// <exception cref="ArgumentNullException">connection is null.</exception>
        /// <exception cref="ArgumentException">connection is in an invalid state (must be Open).</exception>
        /// <exception cref="ArgumentNullException">commandText is null.</exception>
        /// <exception cref="ArgumentException">commandText is whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 0.</exception>
        /// <exception cref="ArgumentException">transaction is invalid (has been rolled back or committed).</exception>
        /// <exception cref="ArgumentException">transaction is using a different connection than the specified connection.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// From this method:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        /// <exception cref="InvalidOperationException">Connection is pending a local transaction.</exception>
        public static int ExecuteNonQuery(IDbConnection connection, string commandText, IDataParameter[] commandParameters = null, CommandType commandType = CommandType.Text, IDbTransaction transaction = null, bool prepareCommand = false, int timeoutSeconds = 0)
        {
            using (IDbCommand command = BuildCommand(connection, commandText, commandParameters, commandType, transaction, prepareCommand, timeoutSeconds))
            {
                return command.ExecuteNonQuery();  // can throw SqlException
            }
        }

        /// <summary>
        /// Opens a connection to the database and executes a SQL statement, returning the number of rows affected.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="IDbConnection"/> to open.  Must be a class.</typeparam>
        /// <param name="connectionString">String used to open a connection to the database.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.  Null parameters are ignored.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns the number of rows affected.</returns>
        /// From OpenConnection:
        /// <exception cref="ArgumentNullException">connectionString is null.</exception>
        /// <exception cref="ArgumentException">connectionString is whitespace.</exception>
        /// <exception cref="ArgumentException">connectionString isn't formatted properly.</exception>
        /// <exception cref="SqlException">A connection-level error occurred while opening the connection. If the Number property contains the value 18487 or 18488, this indicates that the specified password has expired or must be reset.</exception>
        /// From BuildCommand via ExecuteNonQuery:
        /// <exception cref="ArgumentNullException">commandText is null.</exception>
        /// <exception cref="ArgumentException">commandText is whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 0.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// From ExecuteNonQuery:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        public static int ExecuteNonQuery<T>(string connectionString, string commandText, IDataParameter[] commandParameters = null, CommandType commandType = CommandType.Text, bool prepareCommand = false, int timeoutSeconds = 0) where T : class, IDbConnection, new()
        {
            using (var connection = OpenConnection<T>(connectionString))
            {
                int result = ExecuteNonQuery(connection, commandText, commandParameters, commandType, null, prepareCommand, timeoutSeconds);
                connection.Close();
                return result;
            }
        }

        /// <summary>
        /// Determines if a command results in one or more rows of data when executed against a connection.
        /// </summary>
        /// <param name="connection">An <see cref="IDbConnection"/> that represents the connection to a database.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="transaction">The transaction within which the command will execute.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns true if the command results in one or more rows of data.  Returns false if not.</returns>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentNullException">connection is null.</exception>
        /// <exception cref="ArgumentException">connection is in an invalid state (must be Open).</exception>
        /// <exception cref="ArgumentNullException">commandText is null.</exception>
        /// <exception cref="ArgumentException">commandText is whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 0.</exception>
        /// <exception cref="ArgumentException">transaction is invalid (has been rolled back or committed).</exception>
        /// <exception cref="ArgumentException">transaction is using a different connection than the specified connection.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// From ExecuteReader:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// <exception cref="InvalidOperationException">Connection is pending a local transaction.</exception>
        /// <exception cref="InvalidOperationException">There is an open SqlDataReader associated with the connection.</exception>
        /// From this method:
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        public static bool CommandHasRows(IDbConnection connection, string commandText, IDataParameter[] commandParameters = null, CommandType commandType = CommandType.Text, IDbTransaction transaction = null, bool prepareCommand = false, int timeoutSeconds = 0)
        {
            using (IDataReader reader = ExecuteReader(connection, commandText, commandParameters, commandType, transaction, CommandBehavior.Default, prepareCommand, timeoutSeconds))
            {
                return DataReaderHasRows(reader);
            }
        }

        /// <summary>
        /// Opens a connection to the database and determines if a command results in one or more rows of data.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="IDbConnection"/> to open.  Must be a class.</typeparam>
        /// <remarks>
        /// Sets CommandBehavior = CommandBehavior.CloseConnection so that the created connection is closed when the data reader is closed.
        /// </remarks>
        /// <param name="connectionString">String used to open a connection to the database.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns true if the command results in one or more rows of data.  Returns false if not.</returns>
        /// From OpenConnection via ExecuteReader:
        /// <exception cref="ArgumentNullException">connectionString is null.</exception>
        /// <exception cref="ArgumentException">connectionString is whitespace.</exception>
        /// <exception cref="ArgumentException">connectionString isn't formatted properly.</exception>
        /// <exception cref="SqlException">A connection-level error occurred while opening the connection. If the Number property contains the value 18487 or 18488, this indicates that the specified password has expired or must be reset.</exception>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentNullException">commandText is null.</exception>
        /// <exception cref="ArgumentException">commandText is whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 0.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// From ExecuteReader:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// From this method:
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        public static bool CommandHasRows<T>(string connectionString, string commandText, IDataParameter[] commandParameters = null, CommandType commandType = CommandType.Text, bool prepareCommand = false, int timeoutSeconds = 0) where T : class, IDbConnection, new()
        {
            using (IDataReader reader = ExecuteReader<T>(connectionString, commandText, commandParameters, commandType, CommandBehavior.CloseConnection, prepareCommand, timeoutSeconds))
            {
                return DataReaderHasRows(reader);
            }
        }

        /// <summary>
        /// Executes the command text against a connection and returns a single column of values.
        /// </summary>
        /// <param name="connection">An <see cref="IDbConnection"/> that represents the connection to a database.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="transaction">The transaction within which the command will execute.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns a <see cref="Collection{T}"/> where each item corresponds to a value in the result of the query.</returns>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentNullException">connection is null.</exception>
        /// <exception cref="ArgumentException">connection is in an invalid state (must be Open).</exception>
        /// <exception cref="ArgumentNullException">commandText is null.</exception>
        /// <exception cref="ArgumentException">commandText is whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 0.</exception>
        /// <exception cref="ArgumentException">transaction is invalid (has been rolled back or committed).</exception>
        /// <exception cref="ArgumentException">transaction is using a different connection than the specified connection.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// From ExecuteReader:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// <exception cref="InvalidOperationException">Connection is pending a local transaction.</exception>
        /// <exception cref="InvalidOperationException">There is an open SqlDataReader associated with the connection.</exception>
        /// From this method:
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        /// <exception cref="InvalidOperationException">Query results in more than one column.</exception>
        /// <exception cref="InvalidOperationException">Query results in no rows.</exception>
        public static Collection<object> ReadSingleColumn(IDbConnection connection, string commandText, IDataParameter[] commandParameters = null, CommandType commandType = CommandType.Text, IDbTransaction transaction = null, bool prepareCommand = false, int timeoutSeconds = 0)
        {
            using (IDataReader reader = ExecuteReader(connection, commandText, commandParameters, commandType, transaction, CommandBehavior.Default, prepareCommand, timeoutSeconds))
            {
                return ReadSingleColumn(reader);
            }
        }

        /// <summary>
        /// Opens a connection to the database and executes the command text to return a single column of values.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="IDbConnection"/> to open.  Must be a class.</typeparam>
        /// <remarks>
        /// Sets CommandBehavior = CommandBehavior.CloseConnection so that the created connection is closed when the data reader is closed.
        /// </remarks>
        /// <param name="connectionString">String used to open a connection to the database.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns a <see cref="Collection{T}"/> where each item corresponds to a value in the result of the query.</returns>
        /// From OpenConnection via ExecuteReader:
        /// <exception cref="ArgumentNullException">connectionString is null.</exception>
        /// <exception cref="ArgumentException">connectionString is whitespace.</exception>
        /// <exception cref="ArgumentException">connectionString isn't formatted properly.</exception>
        /// <exception cref="SqlException">A connection-level error occurred while opening the connection. If the Number property contains the value 18487 or 18488, this indicates that the specified password has expired or must be reset.</exception>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentNullException">commandText is null.</exception>
        /// <exception cref="ArgumentException">commandText is whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 0.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// From ExecuteReader:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// From this method:
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        /// <exception cref="InvalidOperationException">Query results in more than one column.</exception>
        /// <exception cref="InvalidOperationException">Query results in no rows.</exception>
        public static Collection<object> ReadSingleColumn<T>(string connectionString, string commandText, IDataParameter[] commandParameters = null, CommandType commandType = CommandType.Text, bool prepareCommand = false, int timeoutSeconds = 0) where T : class, IDbConnection, new()
        {
            using (IDataReader reader = ExecuteReader<T>(connectionString, commandText, commandParameters, commandType, CommandBehavior.CloseConnection, prepareCommand, timeoutSeconds))
            {
                return ReadSingleColumn(reader);
            }
        }

        /// <summary>
        /// Executes the command text against a connection and returns the resulting value.
        /// </summary>
        /// <param name="connection">An <see cref="IDbConnection"/> that represents the connection to a database.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="transaction">The transaction within which the command will execute.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns the resulting value.</returns>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentNullException">connection is null.</exception>
        /// <exception cref="ArgumentException">connection is in an invalid state (must be Open).</exception>
        /// <exception cref="ArgumentNullException">commandText is null.</exception>
        /// <exception cref="ArgumentException">commandText is whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 0.</exception>
        /// <exception cref="ArgumentException">transaction is invalid (has been rolled back or committed).</exception>
        /// <exception cref="ArgumentException">transaction is using a different connection than the specified connection.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// From ExecuteReader:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// <exception cref="InvalidOperationException">Connection is pending a local transaction.</exception>
        /// <exception cref="InvalidOperationException">There is an open SqlDataReader associated with the connection.</exception>
        /// From this method:
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        /// <exception cref="InvalidOperationException">Query resulted in no rows.</exception>
        /// <exception cref="InvalidOperationException">Query results in more than one column.</exception>
        /// <exception cref="InvalidOperationException">Query results in more than one row.</exception>
        public static object ReadSingleValue(IDbConnection connection, string commandText, IDataParameter[] commandParameters = null, CommandType commandType = CommandType.Text, IDbTransaction transaction = null, bool prepareCommand = false, int timeoutSeconds = 0)
        {
            using (IDataReader reader = ExecuteReader(connection, commandText, commandParameters, commandType, transaction, CommandBehavior.Default, prepareCommand, timeoutSeconds))
            {
                return ReadSingleValue(reader);
            }
        }

        /// <summary>
        /// Opens a connection to the database and executes the command text, returning the resulting value.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="IDbConnection"/> to open.  Must be a class.</typeparam>
        /// <remarks>
        /// Sets CommandBehavior = CommandBehavior.CloseConnection so that the created connection is closed when the data reader is closed.
        /// </remarks>
        /// <returns>Returns the resulting value.</returns>
        /// <param name="connectionString">String used to open a connection to the database.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// From OpenConnection via ExecuteReader:
        /// <exception cref="ArgumentNullException">connectionString is null.</exception>
        /// <exception cref="ArgumentException">connectionString is whitespace.</exception>
        /// <exception cref="ArgumentException">connectionString isn't formatted properly.</exception>
        /// <exception cref="SqlException">A connection-level error occurred while opening the connection. If the Number property contains the value 18487 or 18488, this indicates that the specified password has expired or must be reset.</exception>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentNullException">commandText is null.</exception>
        /// <exception cref="ArgumentException">commandText is whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 0.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// From ExecuteReader:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// From this method:
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        /// <exception cref="InvalidOperationException">Query results in no rows.</exception>
        /// <exception cref="InvalidOperationException">Query results in more than one column.</exception>
        /// <exception cref="InvalidOperationException">Query results in more than one row.</exception>
        public static object ReadSingleValue<T>(string connectionString, string commandText, IDataParameter[] commandParameters = null, CommandType commandType = CommandType.Text, bool prepareCommand = false, int timeoutSeconds = 0) where T : class, IDbConnection, new()
        {
            using (IDataReader reader = ExecuteReader<T>(connectionString, commandText, commandParameters, commandType, CommandBehavior.CloseConnection, prepareCommand, timeoutSeconds))
            {
                return ReadSingleValue(reader);
            }
        }

        /// <summary>
        /// Rollback a transaction with the proper error handling.
        /// </summary>
        /// <param name="transaction">The transaction to rollback.</param>
        /// <exception cref="ArgumentNullException">transaction is null.</exception>
        /// <exception cref="ArgumentException">transaction is invalid.</exception>
        /// <exception cref="InvalidOperationException">trying to rollback a transaction that has already been committed or rolled back -or- the connection was broken.</exception>
        /// <exception cref="InvalidOperationException">there's an issue rolling back the transaction.</exception>
        public static void RollbackTransaction(SqlTransaction transaction)
        {
            new { transaction }.Must().NotBeNull().OrThrow();

            if (transaction.Connection == null)
            {
                // try to detect invalid transaction before calling transaction.Rollback()
                throw new InvalidOperationException("Could not roll back transaction " + transaction + " because the the transaction has already been committed or rolled back -or- the connection is broken.");
            }

            try
            {
                transaction.Rollback();
            }
            catch (InvalidOperationException)
            {
                // the transaction has already been committed or rolled back -or- the connection is broken.
                throw;
            }
            catch (Exception rollbackException)
            {
                throw new InvalidOperationException("Failed to rollback transaction.", rollbackException);
            }
        }

        /// <summary>
        /// Returns the bit representation of a boolean.
        /// </summary>
        /// <param name="value">The boolean to evaluate.</param>
        /// <returns>Returns "1" if boolean is true, "0" if boolean is false.</returns>
        public static string ToBit(this bool value)
        {
            return value ? "1" : "0";
        }

        /// <summary>
        /// Executes the command text against a connection and returns a single row of values.
        /// </summary>
        /// <param name="connection">An <see cref="IDbConnection"/> that represents the connection to a database.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="transaction">The transaction within which the command will execute.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns a dictionary where the keys are column names and values are the values of the single row returned by the query.</returns>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentNullException">connection is null.</exception>
        /// <exception cref="ArgumentException">connection is in an invalid state (must be Open).</exception>
        /// <exception cref="ArgumentNullException">commandText is null.</exception>
        /// <exception cref="ArgumentException">commandText is whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 0.</exception>
        /// <exception cref="ArgumentException">transaction is invalid (has been rolled back or committed).</exception>
        /// <exception cref="ArgumentException">transaction is using a different connection than the specified connection.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// From ExecuteReader:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// <exception cref="InvalidOperationException">Connection is pending a local transaction.</exception>
        /// <exception cref="InvalidOperationException">There is an open SqlDataReader associated with the connection.</exception>
        /// From this method:
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        /// <exception cref="InvalidOperationException">Query results in now rows.</exception>
        /// <exception cref="InvalidOperationException">Query results in two columns with same name.</exception>
        /// <exception cref="InvalidOperationException">Query results in more than one row.</exception>
        public static Dictionary<string, object> ReadSingleRow(IDbConnection connection, string commandText, IDataParameter[] commandParameters = null, CommandType commandType = CommandType.Text, IDbTransaction transaction = null, bool prepareCommand = false, int timeoutSeconds = 0)
        {
            using (IDataReader reader = ExecuteReader(connection, commandText, commandParameters, commandType, transaction, CommandBehavior.Default, prepareCommand, timeoutSeconds))
            {
                return ReadSingleRow(reader);
            }
        }

        /// <summary>
        /// Opens a connection to the database and executes the command text, returning a single row of values.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="IDbConnection"/> to open.  Must be a class.</typeparam>
        /// <remarks>
        /// Sets CommandBehavior = CommandBehavior.CloseConnection so that the created connection is closed when the data reader is closed.
        /// </remarks>
        /// <param name="connectionString">String used to open a connection to the database.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns a dictionary where the keys are column names and values are the values of the single row returned by the query.</returns>
        /// From OpenConnection via ExecuteReader:
        /// <exception cref="ArgumentNullException">connectionString is null.</exception>
        /// <exception cref="ArgumentException">connectionString is whitespace.</exception>
        /// <exception cref="ArgumentException">connectionString isn't formatted properly.</exception>
        /// <exception cref="SqlException">A connection-level error occurred while opening the connection. If the Number property contains the value 18487 or 18488, this indicates that the specified password has expired or must be reset.</exception>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentNullException">commandText is null.</exception>
        /// <exception cref="ArgumentException">commandText is whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 0.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// From ExecuteReader:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// From this method:
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        /// <exception cref="InvalidOperationException">Query results in now rows.</exception>
        /// <exception cref="InvalidOperationException">Query results in two columns with same name.</exception>
        /// <exception cref="InvalidOperationException">Query results in more than one row.</exception>
        public static Dictionary<string, object> ReadSingleRow<T>(string connectionString, string commandText, IDataParameter[] commandParameters = null, CommandType commandType = CommandType.Text, bool prepareCommand = false, int timeoutSeconds = 0) where T : class, IDbConnection, new()
        {
            using (IDataReader reader = ExecuteReader<T>(connectionString, commandText, commandParameters, commandType, CommandBehavior.CloseConnection, prepareCommand, timeoutSeconds))
            {
                return ReadSingleRow(reader);
            }
        }

        /// <summary>
        /// Writes a result set to a CSV file.
        /// </summary>
        /// <remarks>
        /// Sets CommandBehavior = CommandBehavior.CloseConnection so that the created connection is closed when the data reader is closed.
        /// </remarks>
        /// <param name="connection">An <see cref="IDbConnection"/> that represents the connection to a database.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="outputFilePath">Path to file where CSV data should be written.</param>
        /// <param name="includeColumnNames">Indicates whether the first row should be populated with column names.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="transaction">The transaction within which the command will execute.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentNullException">connection is null.</exception>
        /// <exception cref="ArgumentException">connection is in an invalid state (must be Open).</exception>
        /// <exception cref="ArgumentNullException">commandText is null.</exception>
        /// <exception cref="ArgumentException">commandText is whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 0.</exception>
        /// <exception cref="ArgumentException">transaction is invalid (has been rolled back or committed).</exception>
        /// <exception cref="ArgumentException">transaction is using a different connection than the specified connection.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// From ExecuteReader:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// <exception cref="InvalidOperationException">Connection is pending a local transaction.</exception>
        /// <exception cref="InvalidOperationException">There is an open SqlDataReader associated with the connection.</exception>
        /// From this method:
        /// <exception cref="ArgumentException">outputFilePath is null or whitespace.</exception>
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        /// <exception cref="DirectoryNotFoundException">the specified path is invalid, such as being on an unmapped drive or not existing.</exception>
        /// <exception cref="IOException">I/O error accessing outputFilePath such as when there's a lock on the path.</exception>
        /// <exception cref="UnauthorizedAccessException">Access is denied to outputFilePath.</exception>
        /// <exception cref="SecurityException">the caller does not have the required permission to write to outputFilePath.</exception>
        /// <exception cref="InvalidOperationException">a result set wasn't found when executing the command.  Command is a non-query.</exception>
        public static void WriteToCsv(IDbConnection connection, string commandText, string outputFilePath, bool includeColumnNames = true, IDataParameter[] commandParameters = null, CommandType commandType = CommandType.Text, IDbTransaction transaction = null, bool prepareCommand = false, int timeoutSeconds = 0)
        {
            new { outputFilePath }.Must().NotBeNull().And().NotBeWhiteSpace().OrThrowFirstFailure();

            using (var writer = new StreamWriter(outputFilePath))
            {
                using (IDataReader reader = ExecuteReader(connection, commandText, commandParameters, commandType, transaction, CommandBehavior.Default, prepareCommand, timeoutSeconds))
                {
                    WriteToCsv(reader, writer, includeColumnNames);
                } // using SqlDataReader
            } // using writer
        }

        /// <summary>
        /// Writes a result set to a CSV file.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="IDbConnection"/> to open.  Must be a class.</typeparam>
        /// <remarks>
        /// Sets CommandBehavior = CommandBehavior.CloseConnection so that the created connection is closed when the data reader is closed.
        /// </remarks>
        /// <param name="connectionString">String used to open a connection to the database.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="outputFilePath">Path to file where CSV data should be written.</param>
        /// <param name="includeColumnNames">Indicates whether the first row should be populated with column names.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// From OpenConnection via ExecuteReader:
        /// <exception cref="ArgumentNullException">connectionString is null.</exception>
        /// <exception cref="ArgumentException">connectionString is whitespace.</exception>
        /// <exception cref="ArgumentException">connectionString isn't formatted properly.</exception>
        /// <exception cref="SqlException">A connection-level error occurred while opening the connection. If the Number property contains the value 18487 or 18488, this indicates that the specified password has expired or must be reset.</exception>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentNullException">commandText is null.</exception>
        /// <exception cref="ArgumentException">commandText is whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 0.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// From ExecuteReader:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// From this method:
        /// <exception cref="ArgumentException">outputFilePath is null or whitespace.</exception>
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        /// <exception cref="DirectoryNotFoundException">the specified path is invalid, such as being on an unmapped drive or not existing.</exception>
        /// <exception cref="IOException">I/O error accessing outputFilePath such as when there's a lock on the path.</exception>
        /// <exception cref="UnauthorizedAccessException">Access is denied to outputFilePath.</exception>
        /// <exception cref="SecurityException">the caller does not have the required permission to write to outputFilePath.</exception>
        /// <exception cref="InvalidOperationException">a result set wasn't found when executing the command.  Command is a non-query.</exception>
        public static void WriteToCsv<T>(string connectionString, string commandText, string outputFilePath, bool includeColumnNames = true, IDataParameter[] commandParameters = null, CommandType commandType = CommandType.Text, bool prepareCommand = false, int timeoutSeconds = 0) where T : class, IDbConnection, new()
        {
            new { outputFilePath }.Must().NotBeNull().And().NotBeWhiteSpace().OrThrowFirstFailure();

            using (var writer = new StreamWriter(outputFilePath))
            {
                using (IDataReader reader = ExecuteReader<T>(connectionString, commandText, commandParameters, commandType, CommandBehavior.CloseConnection, prepareCommand, timeoutSeconds))
                {
                    WriteToCsv(reader, writer, includeColumnNames);
                } // using SqlDataReader
            } // using writer
        }

        /// <summary>
        /// Executes an batch SQL command against a connection and returns the total number of rows affected.
        /// </summary>
        /// <remarks>
        /// This method does not support parameters.  Parameter object must be unique per command - parameters cannot be reused across commands.
        /// All commands in the batch are expected to be Text commands.
        /// </remarks>
        /// <returns>Returns the total number of rows affected.</returns>
        /// <param name="connection">An <see cref="IDbConnection"/> that represents the connection to a database.</param>
        /// <param name="batchCommandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="transaction">The transaction within which the command will execute.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// From BuildCommand via ExecuteNonQuery:
        /// <exception cref="ArgumentNullException">connection is null.</exception>
        /// <exception cref="ArgumentException">connection is in an invalid state (must be Open).</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 0.</exception>
        /// <exception cref="ArgumentException">transaction is invalid (has been rolled back or committed).</exception>
        /// <exception cref="ArgumentException">transaction is using a different connection than the specified connection.</exception>
        /// From ExecuteNonQuery:
        /// <exception cref="SqlException">An exception occurred while executing a command in the batch or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// <exception cref="InvalidOperationException">Connection is pending a local transaction.</exception>
        /// From this method:
        /// <exception cref="ArgumentException">batchSqlCommand is null or whitespace.</exception>
        /// <exception cref="InvalidOperationException">no commands found in commandText.</exception>
        public static int ExecuteNonQueryBatch(IDbConnection connection, string batchCommandText, IDbTransaction transaction = null, int timeoutSeconds = 0)
        {
            new { batchCommandText }.Must().NotBeNull().And().NotBeWhiteSpace().OrThrowFirstFailure();

            IEnumerable<string> statements = SqlBatchStatementSplitter.SplitSqlAndRemoveEmptyStatements(batchCommandText);
            // ReSharper disable PossibleMultipleEnumeration
            if (!statements.Any())
            {
                throw new InvalidOperationException("no individual commands found in batchSqlCommand");
            }

            return statements.Sum(statement => ExecuteNonQuery(connection, statement, null, CommandType.Text, transaction, false, timeoutSeconds));
            // ReSharper restore PossibleMultipleEnumeration
        }

        /// <summary>
        /// Opens a connection to the database and executes an batch SQL command, returning the total number of rows affected.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="IDbConnection"/> to open.  Must be a class.</typeparam>
        /// <param name="connectionString">The database connection string.</param>
        /// <param name="batchCommandText">The batch command to execute.</param>
        /// <param name="timeoutSeconds">The command timeout in seconds..</param>
        /// <remarks>
        /// This method does not support parameters.  Parameter object must be unique per command - parameters cannot be reused across commands.
        /// All commands in the batch are expected to be Text commands.
        /// </remarks>
        /// <returns>Returns the total number of rows affected.</returns>
        /// From OpenConnection via ExecuteNonQuery:
        /// <exception cref="ArgumentNullException">connectionString is null.</exception>
        /// <exception cref="ArgumentException">connectionString is whitespace.</exception>
        /// <exception cref="ArgumentException">connectionString isn't formatted properly.</exception>
        /// <exception cref="SqlException">A connection-level error occurred while opening the connection. If the Number property contains the value 18487 or 18488, this indicates that the specified password has expired or must be reset.</exception>
        /// From BuildCommand via ExecuteNonQuery:
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 0.</exception>
        /// From ExecuteNonQuery:
        /// <exception cref="SqlException">An exception occurred while executing a command in the batch or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// From this method:
        /// <exception cref="ArgumentException">batchSqlCommand is null or whitespace.</exception>
        /// <exception cref="InvalidOperationException">no commands found in commandText.</exception>
        public static int ExecuteNonQueryBatch<T>(string connectionString, string batchCommandText, int timeoutSeconds = 0) where T : class, IDbConnection, new()
        {
            new { batchCommandText }.Must().NotBeNull().And().NotBeWhiteSpace().OrThrowFirstFailure();

            IEnumerable<string> statements = SqlBatchStatementSplitter.SplitSqlAndRemoveEmptyStatements(batchCommandText);
            // ReSharper disable PossibleMultipleEnumeration
            if (!statements.Any())
            {
                throw new InvalidOperationException("no individual commands found in batchSqlCommand");
            }

            return statements.Sum(statement => ExecuteNonQuery<T>(connectionString, statement, null, CommandType.Text, false, timeoutSeconds));
            // ReSharper restore PossibleMultipleEnumeration
        }

        /// <summary>
        /// Request a new data parameter from the data source.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="IDbDataParameter"/> to create.</typeparam>
        /// <param name="name">Specifies the parameter name.</param>
        /// <param name="type">Specifies the parameter provider-(in)dependent type.</param>
        /// <param name="value">Specifies the parameter value.</param>
        /// <param name="direction">Specifies the parameter direction.</param>
        /// <param name="size">Specifies the parameter size.</param>
        /// <param name="precision">Specifies the parameter precision.</param>
        /// <param name="scale">Specifies the parameter scale.</param>
        /// <param name="nullable">Specifies the parameter as nullable or not.</param>
        /// <returns>The data parameter with the specified properties set.</returns>
        /// <exception cref="ArgumentNullException">name is null.</exception>
        /// <exception cref="ArgumentException">Parameter name is not 2 characters in length at a minimum.</exception>
        /// <exception cref="ArgumentException">Parameter name does not being with '@'.</exception>
        /// <exception cref="ArgumentException">Parameter name is not alphanumeric.</exception>
        public static T CreateParameter<T>(string name, DbType type, object value, ParameterDirection direction = ParameterDirection.Input, int? size = null, byte? precision = null, byte? scale = null, bool nullable = false) where T : IDbDataParameter, new()
        {
            // check parameters
            new { name }.Must().NotBeNull().And().NotBeWhiteSpace().OrThrowFirstFailure();

            if (name.Length < 2)
            {
                throw new ArgumentException("Parameter name is not 2 characters in length at a minimum.");
            }

            if (name[0] != '@')
            {
                throw new ArgumentException("Parameter name does not being with '@'.");
            }

            if (!name.Substring(1).IsAlphanumeric())
            {
                throw new ArgumentException("Parameter name is not alphanumeric.");
            }

            // create parameter
            // ReSharper disable UseObjectOrCollectionInitializer
            var parameter = new T();
            // ReSharper restore UseObjectOrCollectionInitializer

            // set properties
            parameter.ParameterName = name;
            parameter.Direction = direction;
            parameter.DbType = type;
            if (size != null)
            {
                parameter.Size = (int)size;
            }

            if (precision != null)
            {
                parameter.Precision = (byte)precision;
            }

            if (scale != null)
            {
                parameter.Scale = (byte)scale;
            }

            PropertyInfo pi = parameter.GetType().GetProperty("IsNullable");
            if (pi.CanWrite)
            {
                pi.SetValue(parameter, nullable, null);
            }

            parameter.Value = value ?? DBNull.Value;

            return parameter;
        }

        /// <summary>
        /// Determines if an <see cref="IDataReader"/> has a row to read.
        /// </summary>
        /// <param name="reader">The <see cref="IDataReader"/> to evaluate.</param>
        /// <returns>Returns true if the <see cref="IDataReader"/> has any rows, false if not.</returns>
        private static bool DataReaderHasRows(IDataReader reader)
        {
            bool result = reader.Read();
            reader.Close();
            return result;
        }

        /// <summary>
        /// Reads a single column of values from a data reader.
        /// </summary>
        /// <param name="reader">The <see cref="IDataReader"/> to read from.</param>
        /// <returns>Returns a <see cref="Collection{T}"/> where each item corresponds to a value in the result of the query.</returns>
        /// <exception cref="InvalidOperationException">Query results in more than one column.</exception>
        /// <exception cref="InvalidOperationException">Query results in no rows.</exception>
        private static Collection<object> ReadSingleColumn(IDataReader reader)
        {
            try
            {
                var result = new List<object>();

                if (reader.FieldCount != 1)
                {
                    throw new InvalidOperationException("Query results in more than one column.");
                }

                while (reader.Read())
                {
                    result.Add(reader.IsDBNull(0) ? null : reader[0]);
                }

                if (result.Count == 0)
                {
                    throw new InvalidOperationException("Query results in no rows.");
                }

                return new Collection<object>(result);
            }
            finally
            {
                reader.Close();
            }
        }

        /// <summary>
        /// Reads a single value from a data reader.
        /// </summary>
        /// <param name="reader">The <see cref="IDataReader"/> to read from.</param>
        /// <returns>Returns the resulting value.</returns>
        /// <exception cref="InvalidOperationException">Query results in no rows.</exception>
        /// <exception cref="InvalidOperationException">Query results in more than one column.</exception>
        /// <exception cref="InvalidOperationException">Query results in more than one row.</exception>
        private static object ReadSingleValue(IDataReader reader)
        {
            try
            {
                if (!reader.Read())
                {
                    throw new InvalidOperationException("Query results in no rows.");
                }

                if (reader.FieldCount != 1)
                {
                    throw new InvalidOperationException("Query results in more than one column.");
                }

                object result = null;
                if (!reader.IsDBNull(0))
                {
                    result = reader[0];
                }

                if (reader.Read())
                {
                    throw new InvalidOperationException("Query results in more than one row.");
                }

                return result;
            }
            finally
            {
                reader.Close();
            }
        }

        /// <summary>
        /// Reads a single row of data from a data reader.
        /// Values are returned as a Dictionary where key is the column name (lower-case), and value is the value of that particular column.
        /// </summary>
        /// <param name="reader">The <see cref="IDataReader"/> to read from.</param>
        /// <returns>Returns a dictionary where the keys are column names and values are the values of the single row returned by the query.</returns>
        /// <exception cref="InvalidOperationException">Query results in now rows.</exception>
        /// <exception cref="InvalidOperationException">Query results in two columns with same name.</exception>
        /// <exception cref="InvalidOperationException">Query results in more than one row.</exception>
        private static Dictionary<string, object> ReadSingleRow(IDataReader reader)
        {
            try
            {
                if (!reader.Read())
                {
                    throw new InvalidOperationException("Query results in no rows.");
                }

                var result = new Dictionary<string, object>();
                for (int x = 0; x < reader.FieldCount; x++)
                {
                    string fieldName = reader.GetName(x).ToLowerTrimmed();
                    if (result.ContainsKey(fieldName))
                    {
                        throw new InvalidOperationException("Query results in two columns with the same name.");
                    }

                    // ReSharper disable AssignNullToNotNullAttribute
                    result.Add(fieldName, reader.IsDBNull(x) ? null : reader[x]);
                    // ReSharper restore AssignNullToNotNullAttribute
                }

                if (reader.Read())
                {
                    throw new InvalidOperationException("Query results in more than one row.");
                }

                return result;
            }
            finally
            {
                reader.Close();
            }
        }

        private static void WriteToCsv(IDataReader reader, StreamWriter writer, bool includeColumnNames)
        {
            try
            {
                if (reader.FieldCount == 0)
                {
                    throw new InvalidOperationException("A result set wasn't found when executing the command.  Command is a non-query.");
                }

                // write headers
                if (includeColumnNames)
                {
                    var headers = new List<string>();
                    for (int x = 0; x < reader.FieldCount; x++)
                    {
                        headers.Add(reader.GetName(x));
                    }

                    writer.Write(headers.ToCsv());
                }

                // write content
                while (reader.Read())
                {
                    var rowValues = new List<string>();
                    for (int x = 0; x < reader.FieldCount; x++)
                    {
                        if (reader.IsDBNull(x))
                        {
                            rowValues.Add(null);
                        }
                        else
                        {
                            object value = reader.GetValue(x);

                            // strings, chars, and char arrays need to be made CSV-safe.
                            // other datatypes are guaranteed to never violate CSV-safety rules.
                            var stringValue = value as string;
                            var charArrayValue = value as char[];
                            if (stringValue != null)
                            {
                                rowValues.Add(stringValue.ToCsvSafe());
                            }
                            else if (value is char)
                            {
                                rowValues.Add(value.ToString().ToCsvSafe());
                            }
                            else if (charArrayValue != null)
                            {
                                rowValues.Add(charArrayValue.Select(val => val.ToString(CultureInfo.InvariantCulture)).ToDelimitedString(string.Empty).ToCsvSafe());
                            }
                            else if (value is DateTime)
                            {
                                // DateTime.ToString() will truncate time.
                                var valueAsDate = (DateTime)value;
                                var dateAsString = string.Empty;
                                if (valueAsDate.Kind == DateTimeKind.Unspecified)
                                {
                                    dateAsString = valueAsDate.ToString("yyyy-MM-dd HH:mm:ss.fff");
                                }
                                else if (valueAsDate.Kind == DateTimeKind.Local)
                                {
                                    dateAsString = valueAsDate.ToString("yyyy-MM-dd HH:mm:ss.fffzzz");
                                }
                                else if (valueAsDate.Kind == DateTimeKind.Utc)
                                {
                                    dateAsString = valueAsDate.ToString("yyyy-MM-dd HH:mm:ss.fffZ");
                                }

                                rowValues.Add(dateAsString);
                            }
                            else
                            {
                                rowValues.Add(value.ToString());
                            }
                        } // cell is null?
                    } // for each column in the row
                    writer.WriteLine();

                    // since we already treated strings for CSV-safety, use ToDelimitedString() instead of ToCsv()
                    writer.Write(rowValues.ToDelimitedString(","));
                } // while rows to read
            }
            finally
            {
                reader.Close();
            }
        }
    }
}