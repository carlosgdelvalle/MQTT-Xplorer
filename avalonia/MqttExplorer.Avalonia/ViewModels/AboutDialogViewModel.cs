using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MqttExplorer.Avalonia.ViewModels;

public sealed class AboutDialogViewModel
{
    private AboutDialogViewModel(
        string version,
        string company,
        string copyright,
        string reportIssueMailTo)
    {
        Version = version;
        Company = company;
        Copyright = copyright;
        ReportIssueMailTo = reportIssueMailTo;
    }

    public string Version { get; }
    public string Company { get; }
    public string Copyright { get; }
    public string ReportIssueMailTo { get; }

    public string AppTitle => "Acerca de Explorador MQTT";
    public string Description => "Cliente de escritorio para explorar, monitorear y publicar mensajes MQTT emitidos por ZeiterLink.";
    public string VersionText => $"Version: {Version}";
    public string AuthorText => $"Autor/Empresa: {Company}";
    public string CopyrightText => $"Copyright: {Copyright}";
    public string CreditsText => "Basado en Avalonia, MQTTnet.";
    public string ReportIssueText => "Reportar problema";

    public static AboutDialogViewModel Create()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var version = NormalizeVersion(assembly.GetName().Version);
        var company = assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;

        var resolvedCompany = string.IsNullOrWhiteSpace(company) ? "CdV/PYV" : company;
        var resolvedCopyright = string.IsNullOrWhiteSpace(copyright) ? "CdV 2026" : copyright;
        var reportIssueMailTo = BuildReportIssueMailTo(version);

        return new AboutDialogViewModel(version, resolvedCompany, resolvedCopyright, reportIssueMailTo);
    }

    private static string NormalizeVersion(Version? version)
    {
        if (version is null)
        {
            return "1.0.0";
        }

        var build = version.Build < 0 ? 0 : version.Build;
        return $"{version.Major}.{version.Minor}.{build}";
    }

    private static string BuildReportIssueMailTo(string version)
    {
        const string recipient = "carlos.delvalle@pyv.systems";
        const string subject = "Reportar problema - Explorador MQTT";

        var body =
            "Descripcion del problema:\n\n\n" +
            "---\n" +
            "Informacion del equipo:\n" +
            $"Sistema operativo: {RuntimeInformation.OSDescription}\n" +
            $"Arquitectura: {RuntimeInformation.OSArchitecture}\n" +
            $"Framework: {RuntimeInformation.FrameworkDescription}\n" +
            $"Version app: {version}\n" +
            $"Equipo: {Environment.MachineName}\n" +
            $"Usuario: {Environment.UserName}\n" +
            $"Fecha: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}\n";

        return $"mailto:{recipient}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
    }
}
