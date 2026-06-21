#!/usr/bin/env bash
# Auto-retry launcher for the Oracle "Always Free" A1 instance.
#
# Oracle's free ARM capacity is intermittent ("Out of capacity for A1.Flex").
# This loops the launch API until capacity appears, then prints the public IP.
# Run it on your Mac after configuring the OCI CLI (see ops/OCI-RETRY.md).
#
#   bash ops/oci-launch-retry.sh
#
# Override any default via env var, e.g.:  OCPUS=2 MEM_GB=12 bash ops/oci-launch-retry.sh
set -euo pipefail

# ---- Settings (sensible defaults; override via env) ----
DISPLAY_NAME="${DISPLAY_NAME:-sakugaVault-vm}"
VCN_NAME="${VCN_NAME:-sakugavault-vcn}"
OCPUS="${OCPUS:-4}"
MEM_GB="${MEM_GB:-24}"
SSH_PUB_KEY="${SSH_PUB_KEY:-$HOME/.ssh/id_ed25519.pub}"
SLEEP_SECONDS="${SLEEP_SECONDS:-60}"
# Compartment defaults to your tenancy root (read from the OCI CLI config).
COMPARTMENT_ID="${COMPARTMENT_ID:-$(awk -F= '/^tenancy/{print $2}' ~/.oci/config | tr -d ' ')}"
# --------------------------------------------------------

command -v oci >/dev/null || { echo "OCI CLI not found. See ops/OCI-RETRY.md (step 1)."; exit 1; }
[ -f "$SSH_PUB_KEY" ] || { echo "SSH public key not found at $SSH_PUB_KEY"; exit 1; }
[ -n "$COMPARTMENT_ID" ] || { echo "Could not read tenancy from ~/.oci/config. Run 'oci setup config' first."; exit 1; }

echo "Resolving availability domain, network, and image..."

AD=$(oci iam availability-domain list --compartment-id "$COMPARTMENT_ID" \
		--query 'data[0].name' --raw-output)

VCN_ID=$(oci network vcn list --compartment-id "$COMPARTMENT_ID" --display-name "$VCN_NAME" \
		--query 'data[0].id' --raw-output)
[ -n "$VCN_ID" ] && [ "$VCN_ID" != "null" ] || { echo "VCN '$VCN_NAME' not found."; exit 1; }

SUBNET_ID=$(oci network subnet list --compartment-id "$COMPARTMENT_ID" --vcn-id "$VCN_ID" \
		--query "data[?contains(\"display-name\", 'Public')].id | [0]" --raw-output)
[ -n "$SUBNET_ID" ] && [ "$SUBNET_ID" != "null" ] || { echo "Public subnet in '$VCN_NAME' not found."; exit 1; }

IMAGE_ID=$(oci compute image list --compartment-id "$COMPARTMENT_ID" \
		--operating-system "Canonical Ubuntu" --operating-system-version "24.04" \
		--shape "VM.Standard.A1.Flex" --sort-by TIMECREATED --sort-order DESC \
		--query 'data[0].id' --raw-output)
[ -n "$IMAGE_ID" ] && [ "$IMAGE_ID" != "null" ] || { echo "Ubuntu 24.04 ARM image not found."; exit 1; }

cat <<INFO
  AD:        $AD
  VCN:       $VCN_NAME
  Subnet:    $SUBNET_ID
  Image:     $IMAGE_ID
  Shape:     VM.Standard.A1.Flex  ${OCPUS} OCPU / ${MEM_GB} GB
Retrying every ${SLEEP_SECONDS}s until A1 capacity frees up. Leave this running; Ctrl-C to stop.
INFO

attempt=0
while true; do
	attempt=$((attempt + 1))
	printf '[%s] attempt %d... ' "$(date '+%H:%M:%S')" "$attempt"

	if INSTANCE_ID=$(oci compute instance launch \
			--availability-domain "$AD" \
			--compartment-id "$COMPARTMENT_ID" \
			--shape "VM.Standard.A1.Flex" \
			--shape-config "{\"ocpus\":$OCPUS,\"memoryInGBs\":$MEM_GB}" \
			--image-id "$IMAGE_ID" \
			--subnet-id "$SUBNET_ID" \
			--assign-public-ip true \
			--display-name "$DISPLAY_NAME" \
			--metadata "{\"ssh_authorized_keys\":\"$(cat "$SSH_PUB_KEY")\"}" \
			--query 'data.id' --raw-output 2>/tmp/oci_launch_err); then
		echo "GOT IT ✅"
		echo "Instance: $INSTANCE_ID"
		echo "Waiting for the public IP..."
		IP=""
		for _ in $(seq 1 40); do
			IP=$(oci compute instance list-vnics --instance-id "$INSTANCE_ID" \
					--query 'data[0]."public-ip"' --raw-output 2>/dev/null || true)
			[ -n "$IP" ] && [ "$IP" != "null" ] && break
			sleep 5
		done
		echo "============================================"
		echo " Public IP: ${IP:-check the console}"
		echo "============================================"
		break
	fi

	err=$(cat /tmp/oci_launch_err)
	if echo "$err" | grep -qiE "Out of (host )?capacity|InternalError|500|too busy"; then
		echo "no capacity, sleeping ${SLEEP_SECONDS}s"
		sleep "$SLEEP_SECONDS"
	else
		echo "STOPPED — non-capacity error:"
		echo "$err"
		exit 1
	fi
done
