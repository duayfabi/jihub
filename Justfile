set shell := ["fish", "-lc"]

default:
  @just --list

# --- .NET ---
clean:
  dotnet clean

release:
  dotnet publish -c Release
  cp -r ./appsettings.json jihub.Worker/bin/Release/net8.0/publish/

ci: clean release

# --- dev ---
proxy:
  mitmproxy -p 8080
  set -x HTTPS_PROXY http://127.0.0.1:8080
  set -x HTTP_PROXY  http://127.0.0.1:8080

proxy-init:
  cat /etc/ssl/certs/ca-certificates.crt \
      ~/.mitmproxy/mitmproxy-ca-cert.pem \
      > ~/.mitmproxy/ca-bundle-with-mitmproxy.crt

# --- jihub ---
repo            := "tipee-2nd-level-support"
owner           := "tipee-sa"
# query           := "project%20%3D%20FRM%20AND%20%28type%20IN%20%28%22Bug%20avec%20suivi%20client%22%2C%20Bug%29%29%20AND%20%28%28status%20is%20EMPTY%29%20OR%20%28status%20NOT%20IN%20%28%22Maybe%20Later%22%2C%20Resolved%20%29%29%29%20AND%20issuekey%20%3D%20FRM-14887"
query           := "project%20%3D%20FRM%20AND%20%28type%20IN%20%28%22Bug%20avec%20suivi%20client%22%2C%20Bug%29%29%20AND%20%28%28status%20is%20EMPTY%29%20OR%20%28status%20NOT%20IN%20%28%22Maybe%20Later%22%2C%20Resolved%20%29%29%29"
max_tickets     := "999"
import_owner    := "tipee-sa"
upload_repo     := "tipee-2nd-level-support"
import_path     := "support"
branch          := "master"
project_number  := "55"
additional_label:= "import:jira-2nd-level-support"

import \
  repo=repo \
  owner=owner \
  query=query \
  max_tickets=max_tickets \
  import_owner=import_owner \
  upload_repo=upload_repo \
  import_path=import_path \
  branch=branch \
  project_number=project_number \
  additional_label=additional_label:
    dotnet jihub.Worker/bin/Release/net8.0/publish/jihub.Worker.dll \
      --repo "{{repo}}" \
      --owner "{{owner}}" \
      --query "{{query}}" \
      --max-results {{max_tickets}} \
      --content-link \
      --export \
      --import-owner "{{import_owner}}" \
      --upload-repo "{{upload_repo}}" \
      --import-path "{{import_path}}" \
      --branch "{{branch}}" \
      --project-number {{project_number}} \
      --additional-label "{{additional_label}}" \
      --link-prs