<#
  共有 spec 正規化ロジック — generate.ps1 と check-spec-drift.ps1 が dot-source して使う。

  「取り込み時にファイルへ適用する正規化」と「ドリフト検知で上流をハッシュ前に適用する正規化」を
  同一の関数に一元化する。両者が乖離すると、正規化差だけで永久に drift 誤検知 (または見逃し) に
  なるため、必ずここ 1 箇所に置く。

  正規化内容 (冪等):
    1. 改行を LF に統一 (手元 CRLF と上流 LF の生バイト比較による全行誤検知を排除)。
    2. フロー配列 `[ ... ]` 直後の未引用 `urn:` スキームを引用符化
       (channel-access-token.yml の SharpYaml パースエラー回避。他 spec には該当が無く no-op)。

  いずれもファイル種別に依存せず全 spec に一律適用してよい (該当しない spec では 2 は無変化)。
#>

function ConvertTo-NormalizedSpec {
  [CmdletBinding()]
  param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)
  # 1) 改行を LF へ。
  $t = ($Text -replace "`r`n", "`n") -replace "`r", "`n"
  # 2) フロー配列内の未引用 urn: を引用符化 (既に引用符付きなら無変化)。
  #    文字クラスからカンマを除外し、複数要素配列 `[ urn:a, urn:b ]` で全体を 1 トークンに
  #    癒着させないようにする（現行 spec は単一要素なので挙動は同一）。
  $t = [regex]::Replace($t, '(?<=\[\s*)(urn:[^\],"'']+?)(?=\s*\])', '"$1"')
  return $t
}

function Get-NormalizedSpecSha256 {
  [CmdletBinding()]
  param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)
  $normalized = ConvertTo-NormalizedSpec -Text $Text
  $sha = [System.Security.Cryptography.SHA256]::Create()
  try {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($normalized)
    return ([System.BitConverter]::ToString($sha.ComputeHash($bytes)) -replace "-", "").ToLowerInvariant()
  } finally { $sha.Dispose() }
}
