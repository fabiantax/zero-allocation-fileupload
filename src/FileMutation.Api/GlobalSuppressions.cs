using System.Diagnostics.CodeAnalysis;

// Baseline, not an exemption. FileMutateEndpoint.ExecuteAsync does five jobs — parse multipart,
// buffer, count bytes, validate, respond — and trips S138 (105 lines), S3776 (cognitive
// complexity 23) and S1541 (cyclomatic complexity 14).
//
// Issue #31 extracts the middle three into a reusable Application service. When it lands, this
// whole file is deleted. Do not add a fourth entry here; fix the method instead.

[assembly: SuppressMessage("Major Code Smell", "S138:Methods should not have too many lines",
    Justification = "Baseline for #31, which extracts the service and removes this file.",
    Scope = "member",
    Target = "~M:FileMutation.Api.Endpoints.FileMutateEndpoint.ExecuteAsync(Microsoft.AspNetCore.Http.HttpContext,FileMutation.Domain.Ports.IFileMutator,Microsoft.Extensions.Options.IOptions{FileMutation.Api.Endpoints.UploadLimits},System.Threading.CancellationToken)~System.Threading.Tasks.Task{Microsoft.AspNetCore.Http.IResult}")]
[assembly: SuppressMessage("Critical Code Smell", "S3776:Cognitive Complexity of methods should not be too high",
    Justification = "Baseline for #31, which extracts the service and removes this file.",
    Scope = "member",
    Target = "~M:FileMutation.Api.Endpoints.FileMutateEndpoint.ExecuteAsync(Microsoft.AspNetCore.Http.HttpContext,FileMutation.Domain.Ports.IFileMutator,Microsoft.Extensions.Options.IOptions{FileMutation.Api.Endpoints.UploadLimits},System.Threading.CancellationToken)~System.Threading.Tasks.Task{Microsoft.AspNetCore.Http.IResult}")]
[assembly: SuppressMessage("Major Code Smell", "S1541:Methods and properties should not be too complex",
    Justification = "Baseline for #31, which extracts the service and removes this file.",
    Scope = "member",
    Target = "~M:FileMutation.Api.Endpoints.FileMutateEndpoint.ExecuteAsync(Microsoft.AspNetCore.Http.HttpContext,FileMutation.Domain.Ports.IFileMutator,Microsoft.Extensions.Options.IOptions{FileMutation.Api.Endpoints.UploadLimits},System.Threading.CancellationToken)~System.Threading.Tasks.Task{Microsoft.AspNetCore.Http.IResult}")]
