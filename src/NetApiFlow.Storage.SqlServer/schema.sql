-- NetApiFlow.Storage.SqlServer schema, for a prefix of "netapiflow_" (the default). If you
-- configured a different TablePrefix, replace it in every table/index/constraint name below
-- before running this script. It is what the package runs on first use when AutoCreateSchema is
-- true, so run it yourself when AutoCreateSchema is false.
--
-- Safe to run again, and safe to run on tables written by an earlier version: it creates what is
-- missing, adds route_key when it is absent, and fills route_key only on rows that do not have one.
-- The GO lines separate batches, for sqlcmd, SSMS and Azure Data Studio.

IF OBJECT_ID('dbo.netapiflow_traces', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.netapiflow_traces (
        trace_id varchar(64) NOT NULL PRIMARY KEY,
        method varchar(16) NOT NULL,
        route_template nvarchar(400) NOT NULL,
        http_status int NULL,
        status varchar(16) NOT NULL,
        started_at datetimeoffset NOT NULL,
        ended_at datetimeoffset NULL,
        duration_micros bigint NULL,
        span_count int NOT NULL,
        truncated bit NOT NULL,
        error_message nvarchar(max) NULL,
        tags nvarchar(max) NOT NULL,
        route_key nvarchar(400) NOT NULL DEFAULT ''
    );

    CREATE INDEX ix_netapiflow_traces_started_at ON dbo.netapiflow_traces (started_at);
    CREATE INDEX ix_netapiflow_traces_status ON dbo.netapiflow_traces (status);
    CREATE INDEX ix_netapiflow_traces_method_route ON dbo.netapiflow_traces (method, route_template);
    CREATE INDEX ix_netapiflow_traces_duration_micros ON dbo.netapiflow_traces (duration_micros);
END;
GO

IF COL_LENGTH('dbo.netapiflow_traces', 'route_key') IS NULL
BEGIN
    ALTER TABLE dbo.netapiflow_traces ADD route_key nvarchar(400) NOT NULL DEFAULT '';
END;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_netapiflow_traces_method_route_key' AND object_id = OBJECT_ID('dbo.netapiflow_traces'))
BEGIN
    CREATE INDEX ix_netapiflow_traces_method_route_key ON dbo.netapiflow_traces (method, route_key);
END;
GO

IF OBJECT_ID('dbo.netapiflow_spans', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.netapiflow_spans (
        trace_id varchar(64) NOT NULL,
        span_id varchar(64) NOT NULL,
        parent_span_id varchar(64) NULL,
        sequence int NOT NULL,
        component nvarchar(max) NOT NULL,
        operation nvarchar(max) NOT NULL,
        type varchar(32) NOT NULL,
        started_at datetimeoffset NOT NULL,
        ended_at datetimeoffset NULL,
        duration_micros bigint NULL,
        status varchar(16) NOT NULL,
        error_type nvarchar(max) NULL,
        error_message nvarchar(max) NULL,
        is_error_origin bit NOT NULL,
        tags nvarchar(max) NOT NULL,
        logs nvarchar(max) NOT NULL,
        CONSTRAINT pk_netapiflow_spans PRIMARY KEY (trace_id, span_id),
        CONSTRAINT fk_netapiflow_spans_trace FOREIGN KEY (trace_id)
            REFERENCES dbo.netapiflow_traces (trace_id) ON DELETE CASCADE
    );

    CREATE INDEX ix_netapiflow_spans_trace_id ON dbo.netapiflow_spans (trace_id);
END;
GO

IF OBJECT_ID('dbo.netapiflow_deploy_annotations', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.netapiflow_deploy_annotations (
        id varchar(64) NOT NULL PRIMARY KEY,
        timestamp datetimeoffset NOT NULL,
        label nvarchar(max) NOT NULL
    );

    CREATE INDEX ix_netapiflow_deploy_annotations_timestamp ON dbo.netapiflow_deploy_annotations (timestamp);
END;
GO

DECLARE @pending TABLE (route_template nvarchar(400) NOT NULL PRIMARY KEY);
DECLARE @resolved TABLE (route_template nvarchar(400) NOT NULL PRIMARY KEY, route_key nvarchar(400) NOT NULL);
DECLARE @template nvarchar(400), @canonical nvarchar(400), @inner nvarchar(400), @name nvarchar(400);
DECLARE @position int, @open int, @close int;

INSERT INTO @pending (route_template)
SELECT DISTINCT route_template FROM dbo.netapiflow_traces WHERE route_key = '';

DECLARE templates CURSOR LOCAL FAST_FORWARD FOR SELECT route_template FROM @pending;
OPEN templates;
FETCH NEXT FROM templates INTO @template;

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @canonical = CASE WHEN LEFT(@template, 1) = '/' THEN @template ELSE '/' + @template END;
    IF LEN(@canonical) > 1 AND RIGHT(@canonical, 1) = '/'
        SET @canonical = LEFT(@canonical, LEN(@canonical) - 1);

    SET @position = 1;
    WHILE @position <= LEN(@canonical)
    BEGIN
        SET @open = CHARINDEX('{', @canonical, @position);
        IF @open = 0 BREAK;
        SET @close = CHARINDEX('}', @canonical, @open + 1);
        IF @close = 0 BREAK;

        SET @inner = SUBSTRING(@canonical, @open + 1, @close - @open - 1);
        IF LEN(@inner) = 0 OR CHARINDEX('{', @inner) > 0
        BEGIN
            SET @position = @open + 1;
            CONTINUE;
        END;

        IF LEFT(@inner, 1) = '*'
            SET @inner = SUBSTRING(@inner, 2, LEN(@inner));

        SET @name = CASE WHEN CHARINDEX(':', @inner) > 0 THEN LEFT(@inner, CHARINDEX(':', @inner) - 1) ELSE @inner END;
        WHILE LEN(@name) > 0 AND RIGHT(@name, 1) = '?'
            SET @name = LEFT(@name, LEN(@name) - 1);

        SET @canonical = LEFT(@canonical, @open - 1) + '{' + @name + '}' + SUBSTRING(@canonical, @close + 1, LEN(@canonical));
        SET @position = @open + LEN(@name) + 2;
    END;

    INSERT INTO @resolved (route_template, route_key) VALUES (@template, @canonical);
    FETCH NEXT FROM templates INTO @template;
END;

CLOSE templates;
DEALLOCATE templates;

UPDATE trace
SET route_key = resolved.route_key
FROM dbo.netapiflow_traces AS trace
INNER JOIN @resolved AS resolved ON resolved.route_template = trace.route_template
WHERE trace.route_key = '';
GO
