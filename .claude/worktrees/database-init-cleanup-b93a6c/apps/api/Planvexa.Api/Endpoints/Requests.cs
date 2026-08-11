namespace Planvexa.Api.Endpoints;

using FluentValidation;
using Planvexa.Modules.Tenancy.Application;
using Planvexa.Modules.Tenancy.Domain;

public sealed record CreateWorkspaceRequest(string Name, string Slug);

public sealed record InviteMemberRequest(string Email, string Role);

public sealed record ChangeMemberRoleRequest(string Role);

public sealed record TransferOwnershipRequest(Guid MembershipId);

public sealed record CreateTeamRequest(string Name, string? Description);

public sealed record UpdateTeamRequest(string Name, string? Description);

public sealed record TeamMemberRequest(Guid UserId);

public sealed class CreateWorkspaceRequestValidator : AbstractValidator<CreateWorkspaceRequest>
{
    public CreateWorkspaceRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).NotEmpty().Matches("^[a-z0-9][a-z0-9-]{0,61}[a-z0-9]$")
            .WithMessage("Slug must be 2-63 chars, lowercase alphanumeric with single hyphens.");
    }
}

public sealed class InviteMemberRequestValidator : AbstractValidator<InviteMemberRequest>
{
    public InviteMemberRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(x => x.Role)
            .Must(role => Enum.TryParse<MembershipRole>(role, ignoreCase: true, out _))
            .WithMessage("Role must be one of: Owner, Admin, Member, Guest.");
    }
}

public sealed class ChangeMemberRoleRequestValidator : AbstractValidator<ChangeMemberRoleRequest>
{
    public ChangeMemberRoleRequestValidator()
    {
        RuleFor(x => x.Role)
            .Must(role => Enum.TryParse<MembershipRole>(role, ignoreCase: true, out _))
            .WithMessage("Role must be one of: Owner, Admin, Member, Guest.");
    }
}

public sealed class TransferOwnershipRequestValidator : AbstractValidator<TransferOwnershipRequest>
{
    public TransferOwnershipRequestValidator()
    {
        RuleFor(x => x.MembershipId).NotEmpty();
    }
}

public sealed class CreateTeamRequestValidator : AbstractValidator<CreateTeamRequest>
{
    public CreateTeamRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public sealed class UpdateTeamRequestValidator : AbstractValidator<UpdateTeamRequest>
{
    public UpdateTeamRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}
