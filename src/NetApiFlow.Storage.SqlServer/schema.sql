-- NetApiFlow.Storage.SqlServer schema, for a prefix of "netapiflow_" (the default). If you
-- configured a different TablePrefix, replace it in every table/index/constraint name below
-- before running this script. Running it is equivalent to what the package creates
-- automatically on first use.

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
        tags nvarchar(max) NOT NULL
    );

    CREATE INDEX ix_netapiflow_traces_started_at ON dbo.netapiflow_traces (started_at);
    CREATE INDEX ix_netapiflow_traces_status ON dbo.netapiflow_traces (status);
    CREATE INDEX ix_netapiflow_traces_method_route ON dbo.netapiflow_traces (method, route_template);
    CREATE INDEX ix_netapiflow_traces_duration_micros ON dbo.netapiflow_traces (duration_micros);
END;

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

IF OBJECT_ID('dbo.netapiflow_deploy_annotations', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.netapiflow_deploy_annotations (
        id varchar(64) NOT NULL PRIMARY KEY,
        timestamp datetimeoffset NOT NULL,
        label nvarchar(max) NOT NULL
    );

    CREATE INDEX ix_netapiflow_deploy_annotations_timestamp ON dbo.netapiflow_deploy_annotations (timestamp);
END;
