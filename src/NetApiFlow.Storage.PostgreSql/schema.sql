-- NetApiFlow.Storage.PostgreSql schema, for a prefix of "netapiflow_" (the default). If you
-- configured a different TablePrefix, replace it in every table/index name below before running
-- this script. It is what the package runs on first use when AutoCreateSchema is true, so run it
-- yourself when AutoCreateSchema is false.
--
-- Safe to run again, and safe to run on tables written by an earlier version: it creates what is
-- missing, adds route_key when it is absent, and fills route_key only on rows that do not have one.

CREATE TABLE IF NOT EXISTS netapiflow_traces (
    trace_id varchar(64) PRIMARY KEY,
    method varchar(16) NOT NULL,
    route_template text NOT NULL,
    http_status int NULL,
    status varchar(16) NOT NULL,
    started_at timestamptz NOT NULL,
    ended_at timestamptz NULL,
    duration_micros bigint NULL,
    span_count int NOT NULL,
    truncated boolean NOT NULL,
    error_message text NULL,
    tags jsonb NOT NULL,
    route_key text NOT NULL DEFAULT ''
);

CREATE INDEX IF NOT EXISTS ix_netapiflow_traces_started_at ON netapiflow_traces (started_at);
CREATE INDEX IF NOT EXISTS ix_netapiflow_traces_status ON netapiflow_traces (status);
CREATE INDEX IF NOT EXISTS ix_netapiflow_traces_method_route ON netapiflow_traces (method, route_template);
CREATE INDEX IF NOT EXISTS ix_netapiflow_traces_duration_micros ON netapiflow_traces (duration_micros);

ALTER TABLE netapiflow_traces ADD COLUMN IF NOT EXISTS route_key text NOT NULL DEFAULT '';
CREATE INDEX IF NOT EXISTS ix_netapiflow_traces_method_route_key ON netapiflow_traces (method, route_key);

CREATE TABLE IF NOT EXISTS netapiflow_spans (
    trace_id varchar(64) NOT NULL REFERENCES netapiflow_traces (trace_id) ON DELETE CASCADE,
    span_id varchar(64) NOT NULL,
    parent_span_id varchar(64) NULL,
    sequence int NOT NULL,
    component text NOT NULL,
    operation text NOT NULL,
    type varchar(32) NOT NULL,
    started_at timestamptz NOT NULL,
    ended_at timestamptz NULL,
    duration_micros bigint NULL,
    status varchar(16) NOT NULL,
    error_type text NULL,
    error_message text NULL,
    is_error_origin boolean NOT NULL,
    tags jsonb NOT NULL,
    logs jsonb NOT NULL,
    PRIMARY KEY (trace_id, span_id)
);

CREATE INDEX IF NOT EXISTS ix_netapiflow_spans_trace_id ON netapiflow_spans (trace_id);

CREATE TABLE IF NOT EXISTS netapiflow_deploy_annotations (
    id varchar(64) PRIMARY KEY,
    timestamp timestamptz NOT NULL,
    label text NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_netapiflow_deploy_annotations_timestamp ON netapiflow_deploy_annotations (timestamp);

UPDATE netapiflow_traces AS trace
SET route_key = regexp_replace(canonical.route, '\{\*?([^{}:?]*)[^{}]*\}', '{\1}', 'g')
FROM (
    SELECT trace_id,
           CASE WHEN length(slashed) > 1 AND right(slashed, 1) = '/' THEN left(slashed, -1) ELSE slashed END AS route
    FROM (
        SELECT trace_id,
               CASE WHEN left(route_template, 1) = '/' THEN route_template ELSE '/' || route_template END AS slashed
        FROM netapiflow_traces
        WHERE route_key = ''
    ) AS prefixed
) AS canonical
WHERE trace.trace_id = canonical.trace_id;
