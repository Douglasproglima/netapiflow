namespace NetApiFlow;

/// <summary>Kind of component a <see cref="Domain.FlowSpan"/> represents.</summary>
public enum ComponentType
{
    /// <summary>A minimal API endpoint.</summary>
    Endpoint,

    /// <summary>An MVC controller action.</summary>
    Controller,

    /// <summary>A use case or command/query handler.</summary>
    UseCase,

    /// <summary>An application service.</summary>
    Service,

    /// <summary>A data access repository.</summary>
    Repository,

    /// <summary>A database command.</summary>
    Database,

    /// <summary>An outbound HTTP call.</summary>
    Http,

    /// <summary>A manually marked step that does not fit another category.</summary>
    Custom,
}
