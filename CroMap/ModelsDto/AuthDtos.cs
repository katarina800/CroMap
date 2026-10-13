// Models/AuthDtos.cs
public class ForgotPasswordDto
{
    public string Email { get; set; }
   
}

public class ResetPasswordDto
{
    /// <summary>E-mail za koji je kod zatražen — kod se provjerava samo za tog korisnika.</summary>
    public string Email { get; set; }
    /// <summary>Kod iz e-maila — samo stariji klijenti; novi šalju <see cref="ResetToken"/>.</summary>
    public string? Code { get; set; }
    /// <summary>Dozvola izdana nakon što je poslužitelj provjerio kod (verify-reset-code).</summary>
    public string? ResetToken { get; set; }
    public string NewPassword { get; set; }
}

public class VerifyResetCodeDto
{
    public string Email { get; set; }
    public string Code { get; set; }
}

public class VerifyEmailDto
{
    public string Email { get; set; }
    public string Code { get; set; }
}