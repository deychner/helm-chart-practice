{{/*
Helper templates. Nothing here renders on its own -- files beginning with an
underscore are treated as partials. Everything below is included by the real
templates with `include`.
*/}}

{{/* Short name of the chart, overridable. */}}
{{- define "health-api.name" -}}
{{- default .Chart.Name .Values.nameOverride | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
Fully qualified resource name. Prefixed with the release name so two releases
of this chart in one cluster never collide. 63 chars is the Kubernetes limit
for a label value, hence the trunc.
*/}}
{{- define "health-api.fullname" -}}
{{- if .Values.fullnameOverride }}
{{- .Values.fullnameOverride | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- $name := default .Chart.Name .Values.nameOverride }}
{{- if contains $name .Release.Name }}
{{- .Release.Name | trunc 63 | trimSuffix "-" }}
{{- else }}
{{- printf "%s-%s" .Release.Name $name | trunc 63 | trimSuffix "-" }}
{{- end }}
{{- end }}
{{- end }}

{{- define "health-api.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" }}
{{- end }}

{{/*
The standard Kubernetes recommended labels. Applied to every object so that
`kubectl get all -l app.kubernetes.io/instance=<release>` finds the whole set.
*/}}
{{- define "health-api.labels" -}}
helm.sh/chart: {{ include "health-api.chart" . }}
{{ include "health-api.selectorLabels" . }}
{{- if .Chart.AppVersion }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
{{- end }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end }}

{{/*
Selector labels are a SUBSET of the labels above, and must never include
anything that changes between releases (like version). A Deployment's
spec.selector is immutable -- if a chart upgrade changes it, the upgrade fails.
*/}}
{{- define "health-api.selectorLabels" -}}
app.kubernetes.io/name: {{ include "health-api.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end }}

{{- define "health-api.serviceAccountName" -}}
{{- if .Values.serviceAccount.create }}
{{- default (include "health-api.fullname" .) .Values.serviceAccount.name }}
{{- else }}
{{- default "default" .Values.serviceAccount.name }}
{{- end }}
{{- end }}

{{/*
Namespace every object is placed in: the chart's own value if set, otherwise
whatever `helm install --namespace` supplied.
*/}}
{{- define "health-api.namespace" -}}
{{- default .Release.Namespace .Values.namespace.name }}
{{- end }}
