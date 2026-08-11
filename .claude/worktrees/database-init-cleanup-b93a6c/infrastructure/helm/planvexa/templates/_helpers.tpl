{{- define "planvexa.name" -}}
planvexa
{{- end -}}

{{- define "planvexa.labels" -}}
app.kubernetes.io/name: {{ include "planvexa.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end -}}

{{- define "planvexa.api.selectorLabels" -}}
app.kubernetes.io/name: {{ include "planvexa.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/component: api
{{- end -}}

{{- define "planvexa.web.selectorLabels" -}}
app.kubernetes.io/name: {{ include "planvexa.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/component: web
{{- end -}}

{{- define "planvexa.api.fullname" -}}
{{ .Release.Name }}-api
{{- end -}}

{{- define "planvexa.web.fullname" -}}
{{ .Release.Name }}-web
{{- end -}}
