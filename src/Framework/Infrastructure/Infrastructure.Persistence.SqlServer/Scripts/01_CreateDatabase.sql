-- =============================================
-- Script: Create SaaStack Database
-- Description: Creates the main SaaStack database for on-premise deployment
-- Prerequisites: SQL Server 2019+ or Azure SQL Database
-- =============================================

-- Check if database exists, create if it doesn't
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'SaaStack')
BEGIN
    CREATE DATABASE [SaaStack]
    COLLATE SQL_Latin1_General_CP1_CI_AS;
    PRINT 'Database [SaaStack] created successfully.';
END
ELSE
BEGIN
    PRINT 'Database [SaaStack] already exists.';
END
GO

-- Switch to the SaaStack database
USE [SaaStack];
GO

PRINT 'Database initialization complete.';
GO
