namespace HealthManager.Application;

internal static class SourceReportCalculations
{
    public static List<ReportRow> Calculate(string id, List<SourceLine> lines, DateOnly from, DateOnly to)
    {
        // The database has already excluded records dated after the cutoff.
        var period = lines.Where(x => x.Date >= from).ToList();
        bool Active(SourceLine x) => (x.Day("inicio") ?? x.Date) <= to && x.Day("vencimento") >= to && (x.Day("abandono") is not DateOnly ended || ended > to);
        bool Open(SourceLine x) => x.Day("entrega") is not DateOnly delivered || delivered > to;
        bool InPeriod(DateOnly? day) => day >= from && day <= to;
        static int Patients(IEnumerable<SourceLine> rows) => rows.Select(x => x.Text("paciente")).Distinct().Count();
        static List<ReportRow> By(IEnumerable<SourceLine> rows, string field, Func<IEnumerable<SourceLine>, ReportMetric[]> metrics) =>
            rows.GroupBy(x => string.IsNullOrEmpty(x.Text(field)) ? "Sem atribuição" : x.Text(field))
                .OrderBy(g => g.Key).Select(g => new ReportRow(g.Key, metrics(g))).ToList();
        static List<ReportRow> Total(IEnumerable<SourceLine> rows, params ReportMetric[] metrics) =>
            rows.Any() ? [new("Total", metrics)] : [];
        static decimal Units(IEnumerable<SourceLine> rows) => rows.Sum(x => x.Number("quantidade")!.Value);
        var deliveries = lines.Where(x => InPeriod(x.Day("entrega")));
        switch (id)
        {
            case "novos-agendamentos":
                return Total(period, new("Pacientes", Patients(period)), new("Reservas pagas", period.Sum(x => x.Number("valor")), "brl"));
            case "demanda":
                var waiting = lines.Where(x => x.Day("saida") is not DateOnly exit || exit > to);
                return Total(lines, new ReportMetric("Pacientes aguardando", Patients(waiting)));
            case "pontualidade":
                if (period.Count == 0) return [];
                static decimal Minutes(SourceLine x, string key) => (decimal)TimeOnly.ParseExact(x.Text(key), "HH:mm").ToTimeSpan().TotalMinutes;
                return Total(period, new("Atraso médio", period.Average(x => Math.Max(0, Minutes(x, "inicio") - Minutes(x, "agendado"))), "min"),
                    new("Espera média", period.Average(x => Minutes(x, "inicio") - Minutes(x, "chegada")), "min"));
            case "conversao-consulta":
                var indicated = period.Where(x => x.Text("consulta") != "");
                return Rate("Pacientes que contrataram", Patients(indicated.Where(x => x.Day("contratacao") <= to)), Patients(indicated));
            case "conversao-prescricoes":
                return Rate("Indicações contratadas", period.Count(x => x.Day("contratacao") <= to), period.Count);
            case "carteira":
                return By(lines.Where(Active), "medico", g => [new("Pacientes ativos", Patients(g))]);
            case "programas-vendidos":
                return By(period, "modalidade", g => [new("Contratos", g.Count())]);
            case "vencimentos":
                return lines.Where(x => Active(x) && x.Day("vencimento") <= to.AddDays(30)
                        && (x.Day("renovacao") is not DateOnly renewed || renewed > to))
                    .OrderBy(x => x.Day("vencimento")).ThenBy(x => x.Reference)
                    .Select(x => new ReportRow($"{x.Reference} · {x.Text("paciente")} · {x.Text("medico")}",
                        [new("Dias até vencer", x.Day("vencimento")!.Value.DayNumber - to.DayNumber)])).ToList();
            case "renovacao":
                var expiring = lines.Where(x => InPeriod(x.Day("vencimento")));
                return Rate("Contratos renovados", expiring.Count(x => x.Day("renovacao") <= to), expiring.Count());
            case "abandono-programa":
                var exposed = lines.Where(x => (x.Day("inicio") ?? x.Date) <= to && x.Day("vencimento") >= from && (x.Day("abandono") is not DateOnly ended || ended >= from));
                return Rate("Contratos abandonados", exposed.Count(x => InPeriod(x.Day("abandono"))), exposed.Count());
            case "servicos":
                return By(period, "servico", g => [new("Contratados", Units(g)), new("Realizados", Units(g.Where(x => !Open(x))))]);
            case "consultas-pendentes":
            case "avaliacoes-pendentes":
                var category = id == "consultas-pendentes" ? "Consulta" : "Avaliacao";
                return By(lines.Where(x => Open(x) && x.Text("categoria") == category), "programa", g => [new("Pendentes", Units(g))]);
            case "obrigacoes":
                return By(lines.Where(x => Open(x) && x.Day("previsto") > to), "servico", g => [new("Unidades a atender", Units(g))]);
            case "receita-futura":
                return By(lines.Where(x => x.Day("vencimento") > to && (x.Day("baixa") is not DateOnly paid || paid > to)),
                    "programa", g => [new("Receita futura", g.Sum(x => x.Number("valor")), "brl")]);
            case "custo-programa":
                return By(deliveries, "programa", g => [new("Custo de execução", CompleteSum(g, "custo"), "brl")]);
            case "margem-modalidade":
                return By(deliveries, "modalidade", Margin);
            case "vendas-injetavel":
            case "vendas-protocolo":
            case "margem-injetavel":
            case "margem-protocolo":
                var kind = id.EndsWith("injetavel") ? "Injetavel" : "Protocolo";
                return By(period.Where(x => x.Text("tipo") == kind), "tratamento", id.StartsWith("margem") ? Margin : Sales);
            case "vendas-prescritor": return By(period, "prescritor", Sales);
            case "vendas-colaborador": return By(period, "colaborador", Sales);
            case "aplicacoes-vendidas":
                return By(period, "tratamento", g => [new("Aplicações vendidas", g.Sum(x => x.Number("unidades")))]);
            case "adesao":
                return Rate("Etapas cumpridas no prazo", period.Count(x => x.Day("execucao") <= x.Date), period.Count);
            case "recompra":
                return lines.GroupBy(x => x.Text("paciente")).Where(g => g.Count() >= 2 && g.Any(x => x.Date >= from))
                    .OrderBy(g => g.Key).Select(g => new ReportRow(g.Key,
                        [new("Compras no período", g.Count(x => x.Date >= from)), new("Compras acumuladas", g.Count())])).ToList();
            case "abandono-tratamento":
                return period.GroupBy(x => (Patient: x.Text("paciente"), Protocol: x.Text("protocolo"))).OrderBy(g => g.Key)
                    .Select(g => new ReportRow($"{g.Key.Patient} · {g.Key.Protocol}", [new("Interrupções", g.Count())])).ToList();
            case "insumos":
                return period.GroupBy(x => (Application: x.Text("aplicacao"), Material: x.Text("insumo"), Unit: x.Text("unidade")))
                    .OrderBy(g => g.Key).Select(g => new ReportRow($"{g.Key.Application} · {g.Key.Material} ({g.Key.Unit})",
                        [new("Consumo", Units(g))])).ToList();
            default: throw new KeyNotFoundException("Relatório desconhecido.");
        }
    }

    internal static decimal? Divide(decimal? numerator, decimal denominator) => denominator == 0 ? null : numerator / denominator;
    internal static List<ReportRow> Rate(string label, int numerator, int denominator) =>
        [new("Total", [new(label, numerator), new("Base de cálculo", denominator), new("Taxa", Divide(numerator * 100m, denominator), "percentual")])];
    private static decimal? CompleteSum(IEnumerable<SourceLine> rows, string key) =>
        rows.Any(x => x.Number(key) == null) ? null : rows.Sum(x => x.Number(key));
    private static ReportMetric[] Sales(IEnumerable<SourceLine> rows) =>
        [new("Vendas", rows.Count()), new("Valor contratado", rows.Sum(x => x.Number("receita")), "brl")];
    private static ReportMetric[] Margin(IEnumerable<SourceLine> rows)
    {
        var revenue = CompleteSum(rows, "receita");
        var cost = CompleteSum(rows, "custo");
        var net = revenue - cost;
        return [new("Receita líquida", revenue, "brl"), new("Custo variável", cost, "brl"), new("Margem", net, "brl"),
            new("Margem percentual", Divide(net * 100, revenue ?? 0), "percentual")];
    }
}
