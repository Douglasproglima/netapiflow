-- NetApiFlow.Storage.PostgreSql schema, for a prefix of "netapiflow_" (the default). If you
-- configured a different TablePrefix, replace it in every table/index name below before running
-- this script. Running it is equivalent to what the package creates automatically on first use.

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
    tags jsonb NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_netapiflow_traces_started_at ON netapiflow_traces (started_at);
CREATE INDEX IF NOT EXISTS ix_netapiflow_traces_status ON netapiflow_traces (status);
CREATE INDEX IF NOT EXISTS ix_netapiflow_traces_method_route ON netapiflow_traces (method, route_template);
CREATE INDEX IF NOT EXISTS ix_netapiflow_traces_duration_micros ON netapiflow_traces (duration_micros);

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
    PRIMARY KEY (trace_id, span_id)
);

CREATE INDEX IF NOT EXISTS ix_netapiflow_spans_trace_id ON netapiflow_spans (trace_id);
