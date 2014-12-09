﻿// --------------------------------------------------------------------------------------------------------------------
// <copyright file="DbHelper.cs" company="OBeautifulCode">
//   Copyright 2014 OBeautifulCode
// </copyright>
// <summary>
//   Provides various methods for interacting with a database.
// </summary>
// --------------------------------------------------------------------------------------------------------------------

namespace OBeautifulCode.Libs.Database
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
    using System.Text;
    using System.Web;

    using CuttingEdge.Conditions;

    using OBeautifulCode.Libs.Collections;
    using OBeautifulCode.Libs.String;

    /// <summary>
    /// Provides various methods for interacting with a database.
    /// </summary>
    public static class DbHelper
    {
        #region Fields (Private)
        
        #endregion

        #region Constructors

        #endregion

        #region Properties

        #endregion

        #region Public Methods

        /// <summary>
        /// Opens an database connection using a Connection String.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="IDbConnection"/> to open.  Must be a class.</typeparam>
        /// <param name="connectionString">String used to open a connection to the database.</param>
        /// <returns>Returns an open connection of the specified <see cref="IDbConnection"/> type.</returns>
        /// <exception cref="ArgumentException">connectionString is null or whitespace.</exception>
        /// <exception cref="ArgumentException">connectionString isn't formatted properly.</exception>
        /// <exception cref="SqlException">A connection-level error occurred while opening the connection. If the Number property contains the value 18487 or 18488, this indicates that the specified password has expired or must be reset.</exception>
        public static T OpenConnection<T>(string connectionString) where T : class, IDbConnection, new()
        {
            Condition.Requires(connectionString, "connectionString").IsNotNullOrWhiteSpace();
            
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
        /// <param name="transaction">The transaction within which the command will execute.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.  Null parameters are ignored.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns the constructed <see cref="IDbCommand"/>.</returns>
        /// <exception cref="ArgumentNullException">connection is null.</exception>
        /// <exception cref="ArgumentException">connection is in an invalid state (must be Open).</exception>
        /// <exception cref="ArgumentException">commandText is null or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 1.</exception>
        /// <exception cref="ArgumentException">transaction is invalid (has been rolled back or committed).</exception>
        /// <exception cref="ArgumentException">transaction is using a different connection than the specified connection.</exception>
        /// <exception cref="ArgumentException">The <see cref="SqlParameter"/> is already contained by another <see cref="SqlParameterCollection"/>.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all parameters to have an explicitly set type.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all variable length parameters to have an explicitly set non-zero Size.</exception>
        /// <exception cref="InvalidOperationException">Prepare requires the command to have a transaction when the connection assigned to the command is in a pending local transaction.  The Transaction property of the command has not been initialized.</exception>
        public static IDbCommand BuildCommand(IDbConnection connection, IDbTransaction transaction, CommandType commandType, string commandText, IEnumerable<IDataParameter> commandParameters, bool prepareCommand, int timeoutSeconds)
        {
            // check arguments
            Condition.Requires(connection, "connection").IsNotNull();            
            if (connection.State != ConnectionState.Open)
            {
                throw new ArgumentException("connection is in an invalid state: " + connection.State + ".  Must be Open.");
            }

            Condition.Requires(commandText, "commandText").IsNotNullOrWhiteSpace();
            if (timeoutSeconds < 1)
            {
                throw new ArgumentOutOfRangeException("timeoutSeconds", "timeoutSeconds must be > 0.");
            }

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
        /// <param name="transaction">The transaction within which the command will execute.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="commandBehavior">Provides a description of the results of the query and its effect on the database.  This enumeration has a FlagsAttribute attribute that allows a bitwise combination of its member values.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns an <see cref="IDataReader"/>.</returns>
        /// From BuildCommand:
        /// <exception cref="ArgumentNullException">connection is null.</exception>
        /// <exception cref="ArgumentException">connection is in an invalid state (must be Open).</exception>
        /// <exception cref="ArgumentException">commandText is null or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 1.</exception>
        /// <exception cref="ArgumentException">transaction is invalid (has been rolled back or committed).</exception>
        /// <exception cref="ArgumentException">transaction is using a different connection than the specified connection.</exception>
        /// <exception cref="ArgumentException">The <see cref="SqlParameter"/> is already contained by another <see cref="SqlParameterCollection"/>.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all parameters to have an explicitly set type.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all variable length parameters to have an explicitly set non-zero Size.</exception>
        /// <exception cref="InvalidOperationException">Prepare requires the command to have a transaction when the connection assigned to the command is in a pending local transaction.  The Transaction property of the command has not been initialized.</exception>
        /// From this method:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// <exception cref="InvalidOperationException">Connection is pending a local transaction.</exception>
        /// <exception cref="InvalidOperationException">There is already an open SqlDataReader associated with the connection.</exception>
        /// <remarks>
        /// If an expected parameter type does not match an actual parameter value's type, ExecuteReader() does not throw <see cref="SqlException"/>.
        /// Instead, a reader with no rows is returned.  Any attempt to Read() will throw an exception.
        /// </remarks>
        public static IDataReader ExecuteReader(IDbConnection connection, IDbTransaction transaction, CommandType commandType, CommandBehavior commandBehavior, string commandText, IDataParameter[] commandParameters, bool prepareCommand, int timeoutSeconds)
        {
            using (IDbCommand command = BuildCommand(connection, transaction, commandType, commandText, commandParameters, prepareCommand, timeoutSeconds))
            {
                return command.ExecuteReader(commandBehavior);  // can throw SqlException
            }
        }

        /// <summary>
        /// Opens a connection to the database and executes the CommandText to build an IDataReader.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="IDbConnection"/> to open.  Must be a class.</typeparam>
        /// <param name="connectionString">String used to open a connection to the database.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="commandBehavior">Provides a description of the results of the query and its effect on the database.  This enumeration has a FlagsAttribute attribute that allows a bitwise combination of its member values.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns an <see cref="IDataReader"/>.</returns>
        /// From OpenDBConnection:
        /// <exception cref="ArgumentException">connectionString is null or whitespace.</exception>
        /// <exception cref="ArgumentException">connectionString isn't formatted properly.</exception>
        /// <exception cref="SqlException">A connection-level error occurred while opening the connection. If the Number property contains the value 18487 or 18488, this indicates that the specified password has expired or must be reset.</exception>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentException">commandText is null or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 1.</exception>
        /// <exception cref="ArgumentException">The <see cref="SqlParameter"/> is already contained by another <see cref="SqlParameterCollection"/>.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all parameters to have an explicitly set type.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all variable length parameters to have an explicitly set non-zero Size.</exception>
        /// <exception cref="InvalidOperationException">Prepare requires the command to have a transaction when the connection assigned to the command is in a pending local transaction.  The Transaction property of the command has not been initialized.</exception>
        /// From ExecuteReader:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// <remarks>
        /// If an expected parameter type does not match an actual parameter value's type, ExecuteReader() does not throw <see cref="SqlException"/>.
        /// Instead, a reader with no rows is returned.  Any attempt to Read() will throw an exception.
        /// </remarks>
        public static IDataReader ExecuteReader<T>(string connectionString, CommandType commandType, CommandBehavior commandBehavior, string commandText, IDataParameter[] commandParameters, bool prepareCommand, int timeoutSeconds) where T : class, IDbConnection, new()
        {
            var connection = OpenConnection<T>(connectionString);
            return ExecuteReader(connection, null, commandType, commandBehavior, commandText, commandParameters, prepareCommand, timeoutSeconds);
        }

        /// <summary>
        /// Executes a SQL statement against a connection object and returns the number of rows affected.
        /// </summary>
        /// <param name="connection">An <see cref="IDbConnection"/> that represents the connection to a database.</param>
        /// <param name="transaction">The transaction within which the command will execute.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.  Null parameters are ignored.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns the number of rows affected.</returns>
        /// From BuildCommand:
        /// <exception cref="ArgumentNullException">connection is null.</exception>
        /// <exception cref="ArgumentException">connection is in an invalid state (must be Open).</exception>
        /// <exception cref="ArgumentException">commandText is null or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 1.</exception>
        /// <exception cref="ArgumentException">transaction is invalid (has been rolled back or committed).</exception>
        /// <exception cref="ArgumentException">transaction is using a different connection than the specified connection.</exception>
        /// <exception cref="ArgumentException">The <see cref="SqlParameter"/> is already contained by another <see cref="SqlParameterCollection"/>.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all parameters to have an explicitly set type.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all variable length parameters to have an explicitly set non-zero Size.</exception>
        /// <exception cref="InvalidOperationException">Prepare requires the command to have a transaction when the connection assigned to the command is in a pending local transaction.  The Transaction property of the command has not been initialized.</exception>
        /// From this method:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        /// <exception cref="InvalidOperationException">Connection is pending a local transaction.</exception>
        public static int ExecuteNonQuery(IDbConnection connection, IDbTransaction transaction, CommandType commandType, string commandText, IDataParameter[] commandParameters, bool prepareCommand, int timeoutSeconds)
        {
            using (IDbCommand command = BuildCommand(connection, transaction, commandType, commandText, commandParameters, prepareCommand, timeoutSeconds))
            {
                return command.ExecuteNonQuery();  // can throw SqlException
            }
        }

        /// <summary>
        /// Opens a connection to the database and executes a SQL statement, returning the number of rows affected.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="IDbConnection"/> to open.  Must be a class.</typeparam>
        /// <param name="connectionString">String used to open a connection to the database.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.  Null parameters are ignored.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns the number of rows affected.</returns>
        /// From OpenDBConnection:
        /// <exception cref="ArgumentException">connectionString is null or whitespace.</exception>
        /// <exception cref="ArgumentException">connectionString isn't formatted properly.</exception>
        /// <exception cref="SqlException">A connection-level error occurred while opening the connection. If the Number property contains the value 18487 or 18488, this indicates that the specified password has expired or must be reset.</exception>
        /// From BuildCommand via ExecuteNonQuery:
        /// <exception cref="ArgumentException">commandText is null or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 1.</exception>
        /// <exception cref="ArgumentException">The <see cref="SqlParameter"/> is already contained by another <see cref="SqlParameterCollection"/>.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all parameters to have an explicitly set type.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all variable length parameters to have an explicitly set non-zero Size.</exception>
        /// <exception cref="InvalidOperationException">Prepare requires the command to have a transaction when the connection assigned to the command is in a pending local transaction.  The Transaction property of the command has not been initialized.</exception>
        /// From ExecuteNonQuery:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        public static int ExecuteNonQuery<T>(string connectionString, CommandType commandType, string commandText, IDataParameter[] commandParameters, bool prepareCommand, int timeoutSeconds) where T : class, IDbConnection, new()
        {
            using (var connection = OpenConnection<T>(connectionString))
            {
                int result = ExecuteNonQuery(connection, null, commandType, commandText, commandParameters, prepareCommand, timeoutSeconds);
                connection.Close();
                return result;
            }
        }

        /// <summary>
        /// Determines if a command results in one or more rows of data when executed against a connection.
        /// </summary>
        /// <param name="connection">An <see cref="IDbConnection"/> that represents the connection to a database.</param>
        /// <param name="transaction">The transaction within which the command will execute.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns true if the command results in one or more rows of data.  Returns false if not.</returns>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentNullException">connection is null.</exception>
        /// <exception cref="ArgumentException">connection is in an invalid state (must be Open).</exception>
        /// <exception cref="ArgumentException">commandText is null or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 1.</exception>
        /// <exception cref="ArgumentException">transaction is invalid (has been rolled back or committed).</exception>
        /// <exception cref="ArgumentException">transaction is using a different connection than the specified connection.</exception>
        /// <exception cref="ArgumentException">The <see cref="SqlParameter"/> is already contained by another <see cref="SqlParameterCollection"/>.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all parameters to have an explicitly set type.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all variable length parameters to have an explicitly set non-zero Size.</exception>
        /// <exception cref="InvalidOperationException">Prepare requires the command to have a transaction when the connection assigned to the command is in a pending local transaction.  The Transaction property of the command has not been initialized.</exception>
        /// From ExecuteReader:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// <exception cref="InvalidOperationException">Connection is pending a local transaction.</exception>        
        /// <exception cref="InvalidOperationException">There is an open SqlDataReader associated with the connection.</exception>
        /// From this method:
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        public static bool CommandHasRows(IDbConnection connection, IDbTransaction transaction, CommandType commandType, string commandText, IDataParameter[] commandParameters, bool prepareCommand, int timeoutSeconds)
        {
            using (IDataReader reader = ExecuteReader(connection, transaction, commandType, CommandBehavior.Default, commandText, commandParameters, prepareCommand, timeoutSeconds))
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
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns true if the command results in one or more rows of data.  Returns false if not.</returns>
        /// From OpenDBConnection via ExecuteReader:
        /// <exception cref="ArgumentException">connectionString is null or whitespace.</exception>
        /// <exception cref="ArgumentException">connectionString isn't formatted properly.</exception>
        /// <exception cref="SqlException">A connection-level error occurred while opening the connection. If the Number property contains the value 18487 or 18488, this indicates that the specified password has expired or must be reset.</exception>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentException">commandText is null or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 1.</exception>
        /// <exception cref="ArgumentException">The <see cref="SqlParameter"/> is already contained by another <see cref="SqlParameterCollection"/>.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all parameters to have an explicitly set type.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all variable length parameters to have an explicitly set non-zero Size.</exception>
        /// <exception cref="InvalidOperationException">Prepare requires the command to have a transaction when the connection assigned to the command is in a pending local transaction.  The Transaction property of the command has not been initialized.</exception>
        /// From ExecuteReader:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// From this method:
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>       
        public static bool CommandHasRows<T>(string connectionString, CommandType commandType, string commandText, IDataParameter[] commandParameters, bool prepareCommand, int timeoutSeconds) where T : class, IDbConnection, new()
        {
            using (IDataReader reader = ExecuteReader<T>(connectionString, commandType, CommandBehavior.CloseConnection, commandText, commandParameters, prepareCommand, timeoutSeconds))
            {
                return DataReaderHasRows(reader);
            }
        }

        /// <summary>
        /// Executes the command text against a connection and returns a single column of values.
        /// </summary>
        /// <param name="connection">An <see cref="IDbConnection"/> that represents the connection to a database.</param>
        /// <param name="transaction">The transaction within which the command will execute.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns a <see cref="Collection{T}"/> where each item corresponds to a value in the result of the query.</returns>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentNullException">connection is null.</exception>
        /// <exception cref="ArgumentException">connection is in an invalid state (must be Open).</exception>
        /// <exception cref="ArgumentException">commandText is null or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 1.</exception>
        /// <exception cref="ArgumentException">transaction is invalid (has been rolled back or committed).</exception>
        /// <exception cref="ArgumentException">transaction is using a different connection than the specified connection.</exception>
        /// <exception cref="ArgumentException">The <see cref="SqlParameter"/> is already contained by another <see cref="SqlParameterCollection"/>.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all parameters to have an explicitly set type.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all variable length parameters to have an explicitly set non-zero Size.</exception>
        /// <exception cref="InvalidOperationException">Prepare requires the command to have a transaction when the connection assigned to the command is in a pending local transaction.  The Transaction property of the command has not been initialized.</exception>
        /// From ExecuteReader:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// <exception cref="InvalidOperationException">Connection is pending a local transaction.</exception>        
        /// <exception cref="InvalidOperationException">There is an open SqlDataReader associated with the connection.</exception>
        /// From this method:
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        /// <exception cref="InvalidOperationException">Query results in more than one column.</exception>
        /// <exception cref="InvalidOperationException">Query results in no rows.</exception>
        public static Collection<object> ReadSingleColumn(IDbConnection connection, IDbTransaction transaction, CommandType commandType, string commandText, IDataParameter[] commandParameters, bool prepareCommand, int timeoutSeconds)
        {
            using (IDataReader reader = ExecuteReader(connection, transaction, commandType, CommandBehavior.Default, commandText, commandParameters, prepareCommand, timeoutSeconds))
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
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns a <see cref="Collection{T}"/> where each item corresponds to a value in the result of the query.</returns>
        /// From OpenDBConnection via ExecuteReader:
        /// <exception cref="ArgumentException">connectionString is null or whitespace.</exception>
        /// <exception cref="ArgumentException">connectionString isn't formatted properly.</exception>
        /// <exception cref="SqlException">A connection-level error occurred while opening the connection. If the Number property contains the value 18487 or 18488, this indicates that the specified password has expired or must be reset.</exception>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentException">commandText is null or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 1.</exception>
        /// <exception cref="ArgumentException">The <see cref="SqlParameter"/> is already contained by another <see cref="SqlParameterCollection"/>.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all parameters to have an explicitly set type.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all variable length parameters to have an explicitly set non-zero Size.</exception>
        /// <exception cref="InvalidOperationException">Prepare requires the command to have a transaction when the connection assigned to the command is in a pending local transaction.  The Transaction property of the command has not been initialized.</exception>
        /// From ExecuteReader:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// From this method:
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        /// <exception cref="InvalidOperationException">Query results in more than one column.</exception>
        /// <exception cref="InvalidOperationException">Query results in no rows.</exception>
        public static Collection<object> ReadSingleColumn<T>(string connectionString, CommandType commandType, string commandText, IDataParameter[] commandParameters, bool prepareCommand, int timeoutSeconds) where T : class, IDbConnection, new()
        {
            using (IDataReader reader = ExecuteReader<T>(connectionString, commandType, CommandBehavior.CloseConnection, commandText, commandParameters, prepareCommand, timeoutSeconds))
            {
                return ReadSingleColumn(reader);
            }
        }
        
        /// <summary>
        /// Executes the command text against a connection and returns the resulting value.
        /// </summary>
        /// <param name="connection">An <see cref="IDbConnection"/> that represents the connection to a database.</param>
        /// <param name="transaction">The transaction within which the command will execute.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns the resulting value.</returns>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentNullException">connection is null.</exception>
        /// <exception cref="ArgumentException">connection is in an invalid state (must be Open).</exception>
        /// <exception cref="ArgumentException">commandText is null or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 1.</exception>
        /// <exception cref="ArgumentException">transaction is invalid (has been rolled back or committed).</exception>
        /// <exception cref="ArgumentException">transaction is using a different connection than the specified connection.</exception>
        /// <exception cref="ArgumentException">The <see cref="SqlParameter"/> is already contained by another <see cref="SqlParameterCollection"/>.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all parameters to have an explicitly set type.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all variable length parameters to have an explicitly set non-zero Size.</exception>
        /// <exception cref="InvalidOperationException">Prepare requires the command to have a transaction when the connection assigned to the command is in a pending local transaction.  The Transaction property of the command has not been initialized.</exception>
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
        public static object ReadSingleValue(IDbConnection connection, IDbTransaction transaction, CommandType commandType, string commandText, IDataParameter[] commandParameters, bool prepareCommand, int timeoutSeconds)
        {
            using (IDataReader reader = ExecuteReader(connection, transaction, commandType, CommandBehavior.Default, commandText, commandParameters, prepareCommand, timeoutSeconds))
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
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// From OpenDBConnection via ExecuteReader:
        /// <exception cref="ArgumentException">connectionString is null or whitespace.</exception>
        /// <exception cref="ArgumentException">connectionString isn't formatted properly.</exception>
        /// <exception cref="SqlException">A connection-level error occurred while opening the connection. If the Number property contains the value 18487 or 18488, this indicates that the specified password has expired or must be reset.</exception>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentException">commandText is null or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 1.</exception>
        /// <exception cref="ArgumentException">The <see cref="SqlParameter"/> is already contained by another <see cref="SqlParameterCollection"/>.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all parameters to have an explicitly set type.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all variable length parameters to have an explicitly set non-zero Size.</exception>
        /// <exception cref="InvalidOperationException">Prepare requires the command to have a transaction when the connection assigned to the command is in a pending local transaction.  The Transaction property of the command has not been initialized.</exception>
        /// From ExecuteReader:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// From this method:
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        /// <exception cref="InvalidOperationException">Query results in no rows.</exception>
        /// <exception cref="InvalidOperationException">Query results in more than one column.</exception>
        /// <exception cref="InvalidOperationException">Query results in more than one row.</exception>
        public static object ReadSingleValue<T>(string connectionString, CommandType commandType, string commandText, IDataParameter[] commandParameters, bool prepareCommand, int timeoutSeconds) where T : class, IDbConnection, new()
        {
            using (IDataReader reader = ExecuteReader<T>(connectionString, commandType, CommandBehavior.CloseConnection, commandText, commandParameters, prepareCommand, timeoutSeconds))
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
            Condition.Requires(transaction, "transaction").IsNotNull();

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
        /// Creates an HTML table from an IDataReader
        /// </summary>
        /// <param name="reader">The IDataReader to convert to an HTML table.</param>
        /// <param name="includeHeader">Indicates whether the header should appear in the HTML table.</param>
        /// <param name="htmlEncodeCells">Indicates whether to HTML encode the contents of each sell.  Default is true.</param>
        /// <returns>
        /// Returns an HTML representation of the data pulled from the <see cref="IDataReader"/>.
        /// </returns>
        /// <exception cref="InvalidOperationException">reader is closed</exception>
        public static string ToHtml(this IDataReader reader, bool includeHeader, bool htmlEncodeCells = true)
        {
            if (reader.IsClosed)
            {
                throw new InvalidOperationException("reader is closed.");
            }

            var html = new StringBuilder();
            html.AppendLine("<table>");

            if (includeHeader)
            {
                html.AppendLine("<tr>");
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    html.Append("<th>");
                    html.Append(reader.GetName(i));
                    html.AppendLine("</th>");
                }

                html.AppendLine("</tr>");
            }

            while (reader.Read())
            {
                html.AppendLine("<tr>");
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    html.Append("<td>");
                    html.Append(htmlEncodeCells ? HttpUtility.HtmlEncode(reader[i].ToString()) : reader[i].ToString());
                    html.AppendLine("</td>");
                }

                html.AppendLine("</tr>");
            }

            html.AppendLine("</table>");
            return html.ToString();
        }

        /// <summary>
        /// Executes the command text against a connection and returns a single row of values.
        /// </summary>
        /// <param name="connection">An <see cref="IDbConnection"/> that represents the connection to a database.</param>
        /// <param name="transaction">The transaction within which the command will execute.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns a dictionary where the keys are column names and values are the values of the single row returned by the query.</returns>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentNullException">connection is null.</exception>
        /// <exception cref="ArgumentException">connection is in an invalid state (must be Open).</exception>
        /// <exception cref="ArgumentException">commandText is null or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 1.</exception>
        /// <exception cref="ArgumentException">transaction is invalid (has been rolled back or committed).</exception>
        /// <exception cref="ArgumentException">transaction is using a different connection than the specified connection.</exception>
        /// <exception cref="ArgumentException">The <see cref="SqlParameter"/> is already contained by another <see cref="SqlParameterCollection"/>.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all parameters to have an explicitly set type.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all variable length parameters to have an explicitly set non-zero Size.</exception>
        /// <exception cref="InvalidOperationException">Prepare requires the command to have a transaction when the connection assigned to the command is in a pending local transaction.  The Transaction property of the command has not been initialized.</exception>
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
        public static Dictionary<string, object> ReadSingleRow(IDbConnection connection, IDbTransaction transaction, CommandType commandType, string commandText, IDataParameter[] commandParameters, bool prepareCommand, int timeoutSeconds)
        {
            using (IDataReader reader = ExecuteReader(connection, transaction, commandType, CommandBehavior.Default, commandText, commandParameters, prepareCommand, timeoutSeconds))
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
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <returns>Returns a dictionary where the keys are column names and values are the values of the single row returned by the query.</returns>
        /// From OpenDBConnection via ExecuteReader:
        /// <exception cref="ArgumentException">connectionString is null or whitespace.</exception>
        /// <exception cref="ArgumentException">connectionString isn't formatted properly.</exception>
        /// <exception cref="SqlException">A connection-level error occurred while opening the connection. If the Number property contains the value 18487 or 18488, this indicates that the specified password has expired or must be reset.</exception>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentException">commandText is null or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 1.</exception>
        /// <exception cref="ArgumentException">The <see cref="SqlParameter"/> is already contained by another <see cref="SqlParameterCollection"/>.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all parameters to have an explicitly set type.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all variable length parameters to have an explicitly set non-zero Size.</exception>
        /// <exception cref="InvalidOperationException">Prepare requires the command to have a transaction when the connection assigned to the command is in a pending local transaction.  The Transaction property of the command has not been initialized.</exception>
        /// From ExecuteReader:
        /// <exception cref="SqlException">An exception occurred while executing the command or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// From this method:
        /// <exception cref="SqlException">If parameters specified, type mismatch between variable in command the value of named parameter.</exception>
        /// <exception cref="InvalidOperationException">Query results in now rows.</exception>
        /// <exception cref="InvalidOperationException">Query results in two columns with same name.</exception>
        /// <exception cref="InvalidOperationException">Query results in more than one row.</exception>
        public static Dictionary<string, object> ReadSingleRow<T>(string connectionString, CommandType commandType, string commandText, IDataParameter[] commandParameters, bool prepareCommand, int timeoutSeconds) where T : class, IDbConnection, new()
        {
            using (IDataReader reader = ExecuteReader<T>(connectionString, commandType, CommandBehavior.CloseConnection, commandText, commandParameters, prepareCommand, timeoutSeconds))
            {
                return ReadSingleRow(reader);
            }
        }

        /// <summary>
        /// Writes a result set to a CSV file.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="IDbConnection"/> to open.  Must be a class.</typeparam>
        /// <remarks>
        /// Sets CommandBehavior = CommandBehavior.CloseConnection so that the created connection is closed when the data reader is closed.
        /// </remarks>        
        /// <param name="connectionString">String used to open a connection to the database.</param>
        /// <param name="commandType">Determines how the command text is to be interpreted.</param>
        /// <param name="commandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="commandParameters">A set of parameters to associate with the command.</param>
        /// <param name="prepareCommand">If true, creates a prepared (or compiled) version of the command on the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// <param name="outputFilePath">Path to file where CSV data should be written.</param>
        /// <param name="includeColumnNames">Indicates whether the first row should be populated with column names.</param>
        /// From OpenDBConnection via ExecuteReader:
        /// <exception cref="ArgumentException">connectionString is null or whitespace.</exception>
        /// <exception cref="ArgumentException">connectionString isn't formatted properly.</exception>
        /// <exception cref="SqlException">A connection-level error occurred while opening the connection. If the Number property contains the value 18487 or 18488, this indicates that the specified password has expired or must be reset.</exception>
        /// From BuildCommand via ExecuteReader:
        /// <exception cref="ArgumentException">commandText is null or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 1.</exception>
        /// <exception cref="ArgumentException">The <see cref="SqlParameter"/> is already contained by another <see cref="SqlParameterCollection"/>.</exception>
        /// <exception cref="InvalidOperationException">Attempting to set a parameter of a type that was designed for a data provider other than the provider represented by the specified connection.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all parameters to have an explicitly set type.</exception>
        /// <exception cref="InvalidOperationException">SqlCommand.Prepare method requires all variable length parameters to have an explicitly set non-zero Size.</exception>
        /// <exception cref="InvalidOperationException">Prepare requires the command to have a transaction when the connection assigned to the command is in a pending local transaction.  The Transaction property of the command has not been initialized.</exception>
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
        public static void WriteToCsv<T>(string connectionString, CommandType commandType, string commandText, IDataParameter[] commandParameters, bool prepareCommand, int timeoutSeconds, string outputFilePath, bool includeColumnNames) where T : class, IDbConnection, new()
        {
            Condition.Requires(outputFilePath, "outputFilePath").IsNotNullOrWhiteSpace();
            using (var writer = new StreamWriter(outputFilePath))
            {
                using (IDataReader reader = ExecuteReader<T>(connectionString, commandType, CommandBehavior.CloseConnection, commandText, commandParameters, prepareCommand, timeoutSeconds))
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
                                        rowValues.Add(((DateTime)value).ToString("yyyy-MM-dd HH:mm:ss.fff"));
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
        /// <param name="transaction">The transaction within which the command will execute.</param>
        /// <param name="batchCommandText">The SQL statement, table name, or stored procedure to execute at the data source.</param>
        /// <param name="timeoutSeconds">The wait time, in seconds, before terminating an attempt to execute the command and generating an error.</param>
        /// From BuildCommand via ExecuteNonQuery:
        /// <exception cref="ArgumentNullException">connection is null.</exception>
        /// <exception cref="ArgumentException">connection is in an invalid state (must be Open).</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 1.</exception>
        /// <exception cref="ArgumentException">transaction is invalid (has been rolled back or committed).</exception>
        /// <exception cref="ArgumentException">transaction is using a different connection than the specified connection.</exception>
        /// From BuildCommand:
        /// <exception cref="SqlException">An exception occurred while executing a command in the batch or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// <exception cref="InvalidOperationException">Connection is pending a local transaction.</exception>
        /// From this method:
        /// <exception cref="ArgumentException">batchSqlCommand is null or whitespace.</exception>
        /// <exception cref="InvalidOperationException">no commands found in commandText.</exception>
        public static int ExecuteNonQueryBatch(IDbConnection connection, IDbTransaction transaction, string batchCommandText, int timeoutSeconds)
        {
            Condition.Requires(batchCommandText, "batchCommandText").IsNotNullOrWhiteSpace();
            IEnumerable<string> statements = SqlBatchStatementSplitter.SplitSqlAndRemoveEmptyStatements(batchCommandText);
            // ReSharper disable PossibleMultipleEnumeration
            if (!statements.Any())
            {
                throw new InvalidOperationException("no individual commands found in batchSqlCommand");
            }

            return statements.Sum(statement => ExecuteNonQuery(connection, transaction, CommandType.Text, statement, null, false, timeoutSeconds));
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
        /// From OpenDBConnection via ExecuteNonQuery:
        /// <exception cref="ArgumentException">connectionString is null or whitespace.</exception>
        /// <exception cref="ArgumentException">connectionString isn't formatted properly.</exception>
        /// <exception cref="SqlException">A connection-level error occurred while opening the connection. If the Number property contains the value 18487 or 18488, this indicates that the specified password has expired or must be reset.</exception>
        /// From BuildCommand via ExecuteNonQuery:
        /// <exception cref="ArgumentException">commandText is null or whitespace.</exception>
        /// <exception cref="ArgumentOutOfRangeException">timeoutSeconds is less than 1.</exception>        
        /// From ExecuteNonQuery:
        /// <exception cref="SqlException">An exception occurred while executing a command in the batch or there was a timeout.</exception>
        /// <exception cref="SqlException">A parameter is missing.</exception>
        /// From this method:
        /// <exception cref="ArgumentException">batchSqlCommand is null or whitespace.</exception>
        /// <exception cref="InvalidOperationException">no commands found in commandText.</exception>
        public static int ExecuteNonQueryBatch<T>(string connectionString, string batchCommandText, int timeoutSeconds) where T : class, IDbConnection, new()
        {
            Condition.Requires(batchCommandText, "batchCommandText").IsNotNullOrWhiteSpace();
            IEnumerable<string> statements = SqlBatchStatementSplitter.SplitSqlAndRemoveEmptyStatements(batchCommandText);
            // ReSharper disable PossibleMultipleEnumeration
            if (!statements.Any())
            {
                throw new InvalidOperationException("no individual commands found in batchSqlCommand");
            }

            return statements.Sum(statement => ExecuteNonQuery<T>(connectionString, CommandType.Text, statement, null, false, timeoutSeconds));
            // ReSharper restore PossibleMultipleEnumeration
        }

        /// <summary>
        /// Request a new data parameter from the data source.
        /// </summary>
        /// <typeparam name="T">The type of <see cref="IDbDataParameter"/> to create.</typeparam>
        /// <param name="name">Specifies the parameter name.</param>
        /// <param name="direction">Specifies the parameter direction.</param>
        /// <param name="type">Specifies the parameter provider-(in)dependent type.</param>
        /// <param name="size">Specifies the parameter size.</param>
        /// <param name="precision">Specifies the parameter precision.</param>
        /// <param name="scale">Specifies the parameter scale.</param>
        /// <param name="nullable">Specifies the parameter as nullable or not.</param>        
        /// <param name="value">Specifies the parameter value.</param>
        /// <returns>The data parameter with the specified properties set.</returns>
        /// <exception cref="ArgumentNullException">name is null.</exception>
        /// <exception cref="ArgumentException">Parameter name is not 2 characters in length at a minimum.</exception>
        /// <exception cref="ArgumentException">Parameter name does not being with '@'.</exception>
        /// <exception cref="ArgumentException">Parameter name is not alphanumeric.</exception>
        public static T CreateParameter<T>(string name, ParameterDirection direction, DbType type, int? size, byte? precision, byte? scale, bool nullable, object value) where T : IDbDataParameter, new()
        {
            // check parameters
            Condition.Requires(name, "name").IsNotNull();
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

            parameter.Value = value;

            return parameter;
        }

        #endregion

        #region Internal Methods

        #endregion

        #region Protected Methods

        #endregion

        #region Private Methods

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

        #endregion
    }
}